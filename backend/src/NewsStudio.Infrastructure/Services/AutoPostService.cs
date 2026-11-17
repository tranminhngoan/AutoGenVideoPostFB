using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Domain.Enums;
using NewsStudio.Infrastructure.Common;
using NewsStudio.Infrastructure.Trending;

namespace NewsStudio.Infrastructure.Services;

/// <summary>
/// "Tự động đăng" for several Facebook Pages, each dedicated to one news topic and posting with its own
/// page token. A recurring job (every 2 minutes):
/// <list type="number">
/// <item>LeadMinutes before each page's slot: picks the hottest story OF THAT PAGE'S TOPIC that no page has
/// used yet (no repeated links, and not the same story from another outlet), alternating Vietnam /
/// international news, and starts the normal video pipeline for its link.</item>
/// <item>Videos are made one at a time (AutoPost:MaxConcurrentVideos) across all pages, earliest slot first,
/// so the server isn't overloaded and a worker stays free for the scheduler.</item>
/// <item>When the video is ready and the slot time has come: approves it (the quality check still blocks
/// videos with serious problems) and posts it to that page.</item>
/// </list>
/// A story whose video fails is replaced by the next hottest one (once), while the slot isn't long past.
/// </summary>
public class AutoPostService : IAutoPostService
{
    public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    public const int MaxPostsPerDay = 12;
    /// <summary>Default slots of each newly connected page are shifted by this much, so videos aren't all made at once.</summary>
    public const int StaggerMinutesPerPage = 10;

    public const string Producing = "Producing";
    public const string WaitingSlot = "WaitingSlot";
    public const string Publishing = "Publishing";
    public const string Published = "Published";
    public const string Failed = "Failed";
    public const string Skipped = "Skipped";

    private const int MaxAttempts = 2;
    /// <summary>A slot can still start this late (queue busy, or the server was restarting at the slot time).</summary>
    private static readonly TimeSpan LateWindow = TimeSpan.FromMinutes(90);
    /// <summary>A replacement story is only tried while the slot is less than this far in the past.</summary>
    private static readonly TimeSpan RetryWindow = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan ProduceTimeout = TimeSpan.FromMinutes(75);
    private static readonly TimeSpan PublishingStuckAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RepeatStoryWindow = TimeSpan.FromDays(3);
    private static readonly int[] LookBackHours = { 12, 24, 48 };

    private static readonly SemaphoreSlim TickLock = new(1, 1);

    private readonly IApplicationDbContext _context;
    private readonly ITrendingNewsService _trending;
    private readonly IVideoProjectService _projects;
    private readonly IVideoReviewService _review;
    private readonly IFacebookPublishingService _facebook;
    private readonly IEditorialAssistantService _editorial;
    private readonly IPageInsightsService _insights;
    private readonly NewsStudio.Infrastructure.Ai.AiUsageMeter _meter;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AutoPostService> _logger;

    public AutoPostService(
        IApplicationDbContext context,
        ITrendingNewsService trending,
        IVideoProjectService projects,
        IVideoReviewService review,
        IFacebookPublishingService facebook,
        IEditorialAssistantService editorial,
        IPageInsightsService insights,
        NewsStudio.Infrastructure.Ai.AiUsageMeter meter,
        IConfiguration configuration,
        ILogger<AutoPostService> logger)
    {
        _meter = meter;
        _editorial = editorial;
        _insights = insights;
        _context = context;
        _trending = trending;
        _projects = projects;
        _review = review;
        _facebook = facebook;
        _configuration = configuration;
        _logger = logger;
    }

    private int MaxConcurrentVideos => Math.Clamp(_configuration.GetValue("AutoPost:MaxConcurrentVideos", 1), 1, 4);

    // ---------------------------------------------------------------- schedule helpers

    /// <summary>
    /// Suggested posting times (Vietnam) for a number of posts per day, built around Facebook's busy hours
    /// in Vietnam: breakfast/commute, lunch break, after work, evening prime time and before bed.
    /// <paramref name="offsetMinutes"/> shifts every time (used to stagger several pages).
    /// </summary>
    public static List<string> SuggestSlots(int count, int offsetMinutes = 0)
    {
        count = Math.Clamp(count, 1, MaxPostsPerDay);
        string[] preset = count switch
        {
            1 => new[] { "20:00" },
            2 => new[] { "11:30", "20:00" },
            3 => new[] { "07:00", "12:00", "20:00" },
            4 => new[] { "07:00", "11:30", "17:30", "20:30" },
            5 => new[] { "07:00", "11:30", "17:30", "20:00", "21:30" },
            6 => new[] { "06:30", "09:00", "11:30", "17:30", "20:00", "21:30" },
            7 => new[] { "06:30", "09:00", "11:30", "14:30", "17:30", "20:00", "21:30" },
            8 => new[] { "06:30", "08:30", "11:00", "12:30", "15:00", "17:30", "20:00", "21:30" },
            _ => Array.Empty<string>()
        };
        var times = preset.Length > 0
            ? preset.Select(p => TimeSpan.ParseExact(p, @"hh\:mm", CultureInfo.InvariantCulture)).ToList()
            : Enumerable.Range(0, count) // more than 8: spread evenly between 06:30 and 22:30
                .Select(i => TimeSpan.FromHours(6.5) + TimeSpan.FromMinutes(Math.Round(i * 960.0 / (count - 1) / 5) * 5))
                .ToList();

        var offset = TimeSpan.FromMinutes(((offsetMinutes % 60) + 60) % 60);
        return times
            .Select(t => t + offset)
            .Select(t => t >= TimeSpan.FromDays(1) ? t - TimeSpan.FromDays(1) : t)
            .Select(t => t.ToString(@"hh\:mm", CultureInfo.InvariantCulture))
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
    }

    public static List<string> ParseSlots(string? value) => (value ?? string.Empty)
        .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(s => TimeSpan.TryParseExact(s.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var t)
                     && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1)
            ? t.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
            : null)
        .Where(s => s != null)
        .Select(s => s!)
        .Distinct()
        .OrderBy(s => s, StringComparer.Ordinal)
        .ToList();

    /// <summary>"Mixed" alternates Vietnam (1st, 3rd, 5th… slot) and international news.</summary>
    public static string PlannedRegion(string? mode, int slotIndex) => mode switch
    {
        "VN" => "VN",
        "World" => "World",
        _ => slotIndex % 2 == 0 ? "VN" : "World"
    };

    internal static DateTime VietnamDate(DateTime utc) => (utc + VietnamOffset).Date;

    internal static DateTime DayStartUtc(DateTime vietnamDate) => DateTime.SpecifyKind(vietnamDate - VietnamOffset, DateTimeKind.Utc);

    internal static DateTime SlotUtc(DateTime vietnamDate, string time) =>
        DateTime.SpecifyKind(vietnamDate + TimeSpan.ParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture) - VietnamOffset, DateTimeKind.Utc);

    public static string SlotKey(DateTime vietnamDate, string time, Guid pageId) =>
        $"{vietnamDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {time} {pageId:N}";

    private static string VietnamTime(DateTime utc) => (utc + VietnamOffset).ToString("HH:mm", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- pages & settings

    private sealed record PageSchedule(FacebookAccount Page, AutoPostSettings Settings);

    /// <summary>
    /// Every active connected page with its settings. Pages connected for the first time get settings
    /// (switched off, default times shifted per page); the old single-page settings row is handed to the
    /// first page so an existing schedule keeps working.
    /// </summary>
    private async Task<List<PageSchedule>> LoadPagesAsync(CancellationToken ct)
    {
        var pages = await _context.FacebookAccounts
            .Where(a => a.IsActive)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(ct);
        var all = await _context.AutoPostSettings.OrderBy(s => s.CreatedAtUtc).ToListAsync(ct);
        var legacy = all.FirstOrDefault(s => s.FacebookAccountId == null);

        var result = new List<PageSchedule>();
        var changed = false;
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var settings = all.FirstOrDefault(s => s.FacebookAccountId == page.Id);
            if (settings == null && legacy != null)
            {
                settings = legacy;
                settings.FacebookAccountId = page.Id;
                legacy = null;
                changed = true;
            }
            if (settings == null)
            {
                settings = new AutoPostSettings
                {
                    FacebookAccountId = page.Id,
                    TimeSlots = string.Join(",", SuggestSlots(5, i * StaggerMinutesPerPage))
                };
                _context.AutoPostSettings.Add(settings);
                all.Add(settings);
                changed = true;
            }
            result.Add(new PageSchedule(page, settings));
        }
        if (changed) await _context.SaveChangesAsync(ct);
        return result;
    }

    private static AutoPostSettingsDto ToDto(AutoPostSettings s) => new()
    {
        Enabled = s.Enabled,
        Topic = s.Topic,
        TopicKeywords = s.TopicKeywords,
        PostsPerDay = s.PostsPerDay,
        TimeSlots = ParseSlots(s.TimeSlots),
        RegionMode = s.RegionMode,
        Voice = s.Voice,
        VoiceSpeed = s.VoiceSpeed,
        AiProvider = s.AiProvider,
        TargetDurationSeconds = s.TargetDurationSeconds,
        LeadMinutes = s.LeadMinutes,
        DigestEnabled = s.DigestEnabled,
        DigestTime = s.DigestTime,
        DigestItems = s.DigestItems,
        WeeklyDigestEnabled = s.WeeklyDigestEnabled,
        BreakingEnabled = s.BreakingEnabled,
        BreakingPerDay = s.BreakingPerDay,
        AutoTune = s.AutoTune,
        InstagramEnabled = s.InstagramEnabled,
        WritingTone = s.WritingTone,
        HasLogo = s.BrandLogo is { Length: > 0 }
    };

    /// <summary>Writing tones a page can choose (key → instruction given to the AI writer).</summary>
    public static readonly IReadOnlyDictionary<string, string> WritingTones = new Dictionary<string, string>
    {
        ["neutral"] = "trung tính, khách quan như bản tin truyền hình",
        ["youthful"] = "trẻ trung, gần gũi, câu ngắn, dễ nghe (vẫn nghiêm túc, không dùng tiếng lóng)",
        ["expert"] = "phân tích, chuyên sâu: nêu rõ bối cảnh và ý nghĩa của các con số",
        ["brief"] = "cực kỳ ngắn gọn, đi thẳng vào sự việc, ưu tiên video ngắn"
    };

    public async Task<AutoPostSettingsDto> UpdateSettingsAsync(Guid facebookAccountId, AutoPostSettingsDto dto, CancellationToken cancellationToken = default)
    {
        var schedule = (await LoadPagesAsync(cancellationToken)).FirstOrDefault(p => p.Page.Id == facebookAccountId)
                       ?? throw new KeyNotFoundException("Page này chưa được kết nối hoặc đã ngắt kết nối.");
        var settings = schedule.Settings;

        var topic = NewsTopics.Find(dto.Topic)?.Key ?? NewsTopics.General;
        var keywords = NewsTopics.ParseKeywords(dto.TopicKeywords);
        if (topic == NewsTopics.Custom && keywords.Count == 0 && dto.Enabled)
            throw new ArgumentException("Chủ đề \"Tự chọn từ khoá\" cần ít nhất một từ khoá.");

        var count = Math.Clamp(dto.PostsPerDay, 1, MaxPostsPerDay);
        var slots = ParseSlots(string.Join(",", dto.TimeSlots ?? new List<string>()));
        if (slots.Count != count) slots = SuggestSlots(count);

        settings.Enabled = dto.Enabled;
        settings.Topic = topic;
        settings.TopicKeywords = keywords.Count > 0 ? string.Join(", ", keywords) : null;
        settings.PostsPerDay = count;
        settings.TimeSlots = string.Join(",", slots);
        settings.RegionMode = dto.RegionMode is "VN" or "World" ? dto.RegionMode : "Mixed";
        settings.Voice = string.IsNullOrWhiteSpace(dto.Voice) ? null : dto.Voice.Trim();
        settings.VoiceSpeed = string.IsNullOrWhiteSpace(dto.VoiceSpeed) ? null : dto.VoiceSpeed.Trim();
        settings.AiProvider = string.IsNullOrWhiteSpace(dto.AiProvider) ? null : dto.AiProvider.Trim();
        settings.TargetDurationSeconds = Math.Clamp(dto.TargetDurationSeconds, 30, 150);
        settings.LeadMinutes = Math.Clamp(dto.LeadMinutes, 15, 120);
        settings.DigestEnabled = dto.DigestEnabled;
        settings.DigestTime = ParseSlots(dto.DigestTime ?? "").FirstOrDefault() ?? "20:30";
        settings.DigestItems = Math.Clamp(dto.DigestItems, DailyDigestService.MinItems, DailyDigestService.MaxItems);
        settings.WeeklyDigestEnabled = dto.WeeklyDigestEnabled;
        settings.BreakingEnabled = dto.BreakingEnabled;
        settings.BreakingPerDay = Math.Clamp(dto.BreakingPerDay, 0, MaxBreakingPerDay);
        settings.AutoTune = dto.AutoTune;
        settings.InstagramEnabled = dto.InstagramEnabled;
        settings.WritingTone = WritingTones.ContainsKey(dto.WritingTone ?? "") ? dto.WritingTone! : "neutral";
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Auto-post settings for page {Page}: enabled={Enabled}, topic {Topic}, {Count}/day at {Slots}",
            schedule.Page.PageName, settings.Enabled, settings.Topic, settings.PostsPerDay, settings.TimeSlots);
        return ToDto(settings);
    }

    // ---------------------------------------------------------------- overview

    public async Task<AutoPostOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var pages = await LoadPagesAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var today = VietnamDate(now);
        var dayStart = DayStartUtc(today);

        var runs = await _context.AutoPostRuns.AsNoTracking()
            .Where(r => r.ScheduledAtUtc >= dayStart.AddDays(-7))
            .OrderByDescending(r => r.ScheduledAtUtc)
            .Take(300)
            .ToListAsync(cancellationToken);
        var pageNames = await _context.FacebookAccounts.AsNoTracking()
            .ToDictionaryAsync(a => a.Id, a => a.PageName, cancellationToken);
        var runDtos = await MapRunsAsync(runs, pageNames, cancellationToken);
        var byKey = runDtos.GroupBy(r => r.SlotKey).ToDictionary(g => g.Key, g => g.First());
        var todayRuns = runDtos.Where(r => r.ScheduledAtUtc >= dayStart).ToList();

        var pageDtos = new List<AutoPostPageDto>();
        foreach (var (page, settings) in pages)
        {
            var slots = ParseSlots(settings.TimeSlots);
            var todaySlots = slots.Select((time, i) =>
            {
                var at = SlotUtc(today, time);
                byKey.TryGetValue(SlotKey(today, time, page.Id), out var run);
                return new AutoPostSlotDto
                {
                    Time = time,
                    ScheduledAtUtc = at,
                    PlannedRegion = run?.Region ?? PlannedRegion(settings.RegionMode, i),
                    State = run?.Status ?? (now > at + LateWindow ? "Missed" : "Upcoming"),
                    Run = run
                };
            }).ToList();

            // "Đăng ngay" runs of today also belong on the page's timeline
            foreach (var manual in todayRuns.Where(r => r.IsManual && r.FacebookAccountId == page.Id))
            {
                todaySlots.Add(new AutoPostSlotDto
                {
                    Time = manual.SlotTime,
                    ScheduledAtUtc = manual.ScheduledAtUtc,
                    PlannedRegion = manual.Region ?? "VN",
                    State = manual.Status,
                    Run = manual
                });
            }
            todaySlots = todaySlots.OrderBy(s => s.ScheduledAtUtc).ToList();

            var pageRuns = runDtos.Where(r => r.FacebookAccountId == page.Id).ToList();
            var warnings = new List<string>();
            if (settings.Topic == NewsTopics.Custom && string.IsNullOrWhiteSpace(settings.TopicKeywords))
                warnings.Add("Chủ đề tự chọn nhưng chưa có từ khoá nào.");
            if (page.TokenExpiresAtUtc != null && page.TokenExpiresAtUtc < now.AddDays(7))
                warnings.Add(page.TokenExpiresAtUtc < now
                    ? "Token của Page đã hết hạn. Vào Cài đặt → Facebook để kết nối lại."
                    : $"Token của Page sắp hết hạn ({(page.TokenExpiresAtUtc.Value + VietnamOffset):dd/MM}).");
            if (!string.Equals(page.Status, "Active", StringComparison.OrdinalIgnoreCase))
                warnings.Add($"Kết nối Page đang ở trạng thái \"{page.Status}\"{(string.IsNullOrWhiteSpace(page.ErrorMessage) ? "" : ": " + page.ErrorMessage)}.");

            DateTime? next = null;
            if (settings.Enabled)
            {
                next = todaySlots.Where(s => s.Run == null && s.ScheduledAtUtc > now).Select(s => (DateTime?)s.ScheduledAtUtc).FirstOrDefault();
                if (next == null && slots.Count > 0) next = SlotUtc(today.AddDays(1), slots[0]);
            }

            pageDtos.Add(new AutoPostPageDto
            {
                FacebookAccountId = page.Id,
                PageId = page.PageId,
                PageName = page.PageName,
                TokenStatus = page.Status,
                TokenExpiresAtUtc = page.TokenExpiresAtUtc,
                InstagramUsername = page.InstagramUsername,
                Settings = ToDto(settings),
                TopicName = NewsTopics.NameOf(settings.Topic),
                TodaySlots = todaySlots,
                PublishedToday = pageRuns.Count(r => r.Status == Published && r.ScheduledAtUtc >= dayStart),
                FailedToday = pageRuns.Count(r => r.Status == Failed && r.ScheduledAtUtc >= dayStart),
                NextSlotUtc = next,
                CurrentRun = pageRuns.Where(IsActive).OrderBy(r => r.ScheduledAtUtc).FirstOrDefault(),
                LastPublished = pageRuns.Where(r => r.Status == Published).OrderByDescending(r => r.PublishedAtUtc ?? r.ScheduledAtUtc).FirstOrDefault(),
                Warnings = warnings
            });
        }

        var warningsAll = new List<string>();
        if (pages.Count == 0)
            warningsAll.Add("Chưa kết nối Page Facebook nào. Vào Cài đặt → Facebook để thêm Page (mỗi Page một token riêng).");
        if (!GeminiKeys.HasAny(_configuration) && !SimulationMode.IsEnabled(_configuration))
            warningsAll.Add("Máy chủ chưa có API key Gemini nên không viết được kịch bản.");

        return new AutoPostOverviewDto
        {
            Today = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Pages = pageDtos,
            Totals = new AutoPostTotalsDto
            {
                Pages = pageDtos.Count,
                PagesEnabled = pageDtos.Count(p => p.Settings.Enabled),
                PlannedToday = pageDtos.Where(p => p.Settings.Enabled).Sum(p => p.Settings.PostsPerDay),
                PublishedToday = todayRuns.Count(r => r.Status == Published),
                InProgress = runDtos.Count(IsActive),
                FailedToday = todayRuns.Count(r => r.Status == Failed),
                PublishedLast7Days = runDtos.Count(r => r.Status == Published)
            },
            RecentRuns = runDtos.Take(150).ToList(),
            MaxConcurrentVideos = MaxConcurrentVideos,
            Warnings = warningsAll
        };
    }

    private static bool IsActive(AutoPostRunDto r) => r.Status is Producing or WaitingSlot or Publishing;

    private async Task<List<AutoPostRunDto>> MapRunsAsync(List<AutoPostRun> runs, IReadOnlyDictionary<Guid, string> pageNames, CancellationToken ct)
    {
        var projectIds = runs.Where(r => r.VideoProjectId != null).Select(r => r.VideoProjectId!.Value).Distinct().ToList();
        var projects = await _context.VideoProjects.AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Status })
            .ToDictionaryAsync(p => p.Id, p => p.Status, ct);
        var progress = (await _context.ProcessingJobs.AsNoTracking()
                .Where(j => projectIds.Contains(j.VideoProjectId))
                .Select(j => new { j.VideoProjectId, j.ProgressPercentage, j.StartedAtUtc, j.CreatedAtUtc })
                .ToListAsync(ct))
            .GroupBy(j => j.VideoProjectId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(j => j.StartedAtUtc ?? j.CreatedAtUtc).First().ProgressPercentage);

        return runs.Select(r => new AutoPostRunDto
        {
            Id = r.Id,
            SlotKey = r.SlotKey,
            FacebookAccountId = r.FacebookAccountId,
            PageName = r.FacebookAccountId != null && pageNames.TryGetValue(r.FacebookAccountId.Value, out var name) ? name : null,
            ScheduledAtUtc = r.ScheduledAtUtc,
            SlotTime = VietnamTime(r.ScheduledAtUtc),
            SlotDate = VietnamDate(r.ScheduledAtUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IsManual = r.IsManual,
            IsBreaking = r.IsBreaking,
            Status = r.Status,
            Attempts = r.Attempts,
            ArticleUrl = r.ArticleUrl,
            ArticleTitle = r.ArticleTitle,
            SourceName = r.SourceName,
            Region = r.Region,
            HotScore = r.HotScore,
            VideoProjectId = r.VideoProjectId,
            ProjectStatus = r.VideoProjectId != null && projects.TryGetValue(r.VideoProjectId.Value, out var st) ? st.ToString() : null,
            ProgressPercentage = r.VideoProjectId != null && progress.TryGetValue(r.VideoProjectId.Value, out var pct) ? pct : null,
            FacebookPostUrl = r.FacebookPostUrl,
            StartedAtUtc = r.StartedAtUtc,
            PublishedAtUtc = r.PublishedAtUtc,
            Message = r.Message
        }).ToList();
    }

    // ---------------------------------------------------------------- manual actions

    public async Task<AutoPostRunDto> RunNowAsync(Guid facebookAccountId, CancellationToken cancellationToken = default)
    {
        var schedule = (await LoadPagesAsync(cancellationToken)).FirstOrDefault(p => p.Page.Id == facebookAccountId)
                       ?? throw new KeyNotFoundException("Page này chưa được kết nối hoặc đã ngắt kết nối.");
        var now = DateTime.UtcNow;
        var today = VietnamDate(now);
        var dayStart = DayStartUtc(today);

        // "Mixed": take the region this page posted less today
        var region = schedule.Settings.RegionMode;
        if (region is not ("VN" or "World"))
        {
            var todayRegions = await _context.AutoPostRuns
                .Where(r => r.FacebookAccountId == facebookAccountId && r.ScheduledAtUtc >= dayStart
                            && r.Region != null && r.Status != Failed && r.Status != Skipped)
                .Select(r => r.Region!)
                .ToListAsync(cancellationToken);
            region = todayRegions.Count(r => r == "World") < todayRegions.Count(r => r == "VN") ? "World" : "VN";
        }

        var run = new AutoPostRun
        {
            SlotKey = $"{today:yyyy-MM-dd} now-{(now + VietnamOffset):HHmmss}-{Guid.NewGuid():N}"[..36] + $" {facebookAccountId:N}",
            FacebookAccountId = facebookAccountId,
            ScheduledAtUtc = now,
            IsManual = true,
            Status = Producing,
            StartedAtUtc = now
        };
        _context.AutoPostRuns.Add(run);
        await _context.SaveChangesAsync(cancellationToken);

        await StartProductionAsync(run, schedule.Settings, region, now, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        var names = new Dictionary<Guid, string> { [facebookAccountId] = schedule.Page.PageName };
        return (await MapRunsAsync(new List<AutoPostRun> { run }, names, cancellationToken))[0];
    }

    public async Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await _context.AutoPostRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run == null || run.Status is not (Producing or WaitingSlot)) return false;
        run.Status = Skipped;
        run.Message = "Đã huỷ lượt đăng này. Video (nếu đã làm) vẫn nằm ở trang Video để duyệt tay.";
        run.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ---------------------------------------------------------------- recurring job

    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        // One tick at a time (publishing can take a few minutes; overlapping ticks would double-post).
        if (!await TickLock.WaitAsync(0, cancellationToken)) return;
        try
        {
            var pages = await LoadPagesAsync(cancellationToken);
            var byPage = pages.ToDictionary(p => p.Page.Id);
            var now = DateTime.UtcNow;

            // 1. Move running posts forward
            var active = await _context.AutoPostRuns
                .Where(r => r.Status == Producing || r.Status == WaitingSlot || r.Status == Publishing)
                .OrderBy(r => r.ScheduledAtUtc)
                .ToListAsync(cancellationToken);
            foreach (var run in active)
            {
                try
                {
                    await AdvanceAsync(run, run.FacebookAccountId != null ? byPage.GetValueOrDefault(run.FacebookAccountId.Value) : null, now, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Auto-post run {Slot} failed", run.SlotKey);
                    run.Status = Failed;
                    run.Message = $"Lỗi: {ex.Message}";
                }
                run.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
            }

            // 2. Start due slots of every enabled page, earliest first, while the shared video queue has room
            var producing = active.Count(r => r.Status == Producing);
            if (producing >= MaxConcurrentVideos) return;
            // Monthly AI budget reached and set to "pause": nothing new until next month
            if (await _meter.ShouldPauseAsync(cancellationToken))
            {
                _logger.LogWarning("AI budget reached: auto-post paused until next month");
                return;
            }

            var today = VietnamDate(now);
            var due = new List<(DateTime At, string Key, PageSchedule Schedule, int Index)>();
            foreach (var schedule in pages.Where(p => p.Settings.Enabled))
            {
                var slots = ParseSlots(schedule.Settings.TimeSlots);
                var lead = TimeSpan.FromMinutes(schedule.Settings.LeadMinutes);
                for (var i = 0; i < slots.Count; i++)
                {
                    var at = SlotUtc(today, slots[i]);
                    if (now >= at - lead && now <= at + LateWindow)
                        due.Add((at, SlotKey(today, slots[i], schedule.Page.Id), schedule, i));
                }
            }
            var keys = due.Select(d => d.Key).ToList();
            var started = (await _context.AutoPostRuns.Where(r => keys.Contains(r.SlotKey)).Select(r => r.SlotKey).ToListAsync(cancellationToken))
                .ToHashSet();

            foreach (var slot in due.Where(d => !started.Contains(d.Key)).OrderBy(d => d.At))
            {
                if (producing >= MaxConcurrentVideos) break;

                var run = new AutoPostRun
                {
                    SlotKey = slot.Key,
                    FacebookAccountId = slot.Schedule.Page.Id,
                    ScheduledAtUtc = slot.At,
                    Status = Producing,
                    StartedAtUtc = now
                };
                _context.AutoPostRuns.Add(run);
                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Auto-post slot {Time} started for page {Page}", VietnamTime(slot.At), slot.Schedule.Page.PageName);

                await StartProductionAsync(run, slot.Schedule.Settings, PlannedRegion(slot.Schedule.Settings.RegionMode, slot.Index), now, cancellationToken);
                run.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                if (run.Status == Producing) producing++;
            }

            // 3. Breaking news: a story that suddenly becomes big news is posted right away (scheduled slots first)
            if (producing < MaxConcurrentVideos)
                await StartBreakingNewsAsync(pages, now, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never let the recurring job fail: Hangfire would retry it and log noise; the next tick tries again.
            _logger.LogError(ex, "Auto-post tick failed");
        }
        finally
        {
            TickLock.Release();
        }
    }

    private async Task AdvanceAsync(AutoPostRun run, PageSchedule? schedule, DateTime now, CancellationToken ct)
    {
        if (schedule == null && run.Status is Producing or WaitingSlot)
        {
            run.Status = Failed;
            run.Message = "Page của lượt này đã ngắt kết nối. Video (nếu đã làm) vẫn ở trang Video.";
            return;
        }

        if (schedule != null && !schedule.Settings.Enabled && !run.IsManual && run.Status is Producing or WaitingSlot)
        {
            run.Status = Skipped;
            run.Message = "Đã tắt tự động đăng cho Page này. Video (nếu đã làm) vẫn nằm ở trang Video để duyệt tay.";
            return;
        }

        if (run.VideoProjectId == null)
        {
            run.Status = Failed;
            run.Message ??= "Không có video cho lượt đăng này.";
            return;
        }

        var project = await _context.VideoProjects.AsNoTracking()
            .Where(p => p.Id == run.VideoProjectId)
            .Select(p => new { p.Status, p.ErrorMessage, p.GateStatus, p.GateJson })
            .FirstOrDefaultAsync(ct);
        if (project == null)
        {
            run.Status = Failed;
            run.Message = "Video của lượt này đã bị xoá.";
            return;
        }

        switch (run.Status)
        {
            case Producing:
                switch (project.Status)
                {
                    case VideoProjectStatus.AwaitingApproval when project.GateStatus is QualityGateStatus.ReviewRequired or QualityGateStatus.Rejected:
                        // Only videos that passed every check are posted automatically; this one waits for a
                        // person on the Video page, and the slot takes another story.
                        await RetryOrFailAsync(run, schedule!.Settings, now,
                            $"Video cần người duyệt trước khi đăng (đang chờ ở trang Video): {GateReasons(project.GateJson)}", ct);
                        break;
                    case VideoProjectStatus.AwaitingApproval:
                        if (now >= run.ScheduledAtUtc)
                        {
                            await PublishAsync(run, ct);
                        }
                        else
                        {
                            run.Status = WaitingSlot;
                            run.Message = $"Video đã xong, chờ đến {VietnamTime(run.ScheduledAtUtc)} để đăng.";
                        }
                        break;
                    case VideoProjectStatus.Published:
                        run.Status = Published;
                        run.PublishedAtUtc ??= now;
                        run.Message = "Video đã được đăng.";
                        break;
                    case VideoProjectStatus.Failed:
                    case VideoProjectStatus.Cancelled:
                        await RetryOrFailAsync(run, schedule!.Settings, now, project.ErrorMessage ?? "Dựng video thất bại", ct);
                        break;
                    default:
                        if (run.StartedAtUtc != null && now - run.StartedAtUtc > ProduceTimeout)
                        {
                            await RetryOrFailAsync(run, schedule!.Settings, now, "Làm video quá lâu, có thể bị kẹt", ct);
                        }
                        else
                        {
                            run.Message = $"Đang làm video ({StepLabel(project.Status)}).";
                        }
                        break;
                }
                break;

            case WaitingSlot:
                if (project.Status != VideoProjectStatus.AwaitingApproval)
                {
                    run.Status = project.Status == VideoProjectStatus.Published ? Published : Failed;
                    run.Message = project.Status == VideoProjectStatus.Published
                        ? "Video đã được đăng."
                        : "Video không còn ở trạng thái chờ đăng (đã bị sửa hoặc làm lại).";
                    break;
                }
                if (now >= run.ScheduledAtUtc) await PublishAsync(run, ct);
                break;

            case Publishing:
                // Only reached when a previous tick died mid-upload; publishing is idempotent per video.
                if (now - (run.UpdatedAtUtc ?? run.CreatedAtUtc) > PublishingStuckAfter) await PublishAsync(run, ct);
                break;
        }
    }

    private async Task RetryOrFailAsync(AutoPostRun run, AutoPostSettings settings, DateTime now, string reason, CancellationToken ct)
    {
        if (!run.IsBreaking && run.Attempts < MaxAttempts && now < run.ScheduledAtUtc + RetryWindow)
        {
            _logger.LogWarning("Auto-post {Slot}: story failed ({Reason}); picking another one", run.SlotKey, reason);
            var previousTitle = run.ArticleTitle;
            await StartProductionAsync(run, settings, run.Region ?? "VN", now, ct);
            if (run.Status == Producing)
                run.Message = $"Tin trước (\"{Shorten(previousTitle, 60)}\") lỗi: {Shorten(reason, 160)}. Đã chọn tin khác.";
            return;
        }
        run.Status = Failed;
        run.Message = $"Không làm được video: {Shorten(reason, 300)}";
    }

    private async Task StartProductionAsync(AutoPostRun run, AutoPostSettings settings, string region, DateTime now, CancellationToken ct,
        (TrendingArticleDto Item, Guid? ClusterId)? preselected = null)
    {
        run.Attempts++;

        var keywords = NewsTopics.ParseKeywords(settings.TopicKeywords);
        if (settings.Topic == NewsTopics.Custom && keywords.Count == 0)
        {
            run.Status = Failed;
            run.Message = "Page dùng chủ đề \"Tự chọn từ khoá\" nhưng chưa có từ khoá nào.";
            return;
        }

        // "Tự học": stories of the kinds this page's audience watches most come first
        Func<TrendingArticleDto, double>? weight = null;
        if (preselected == null && settings.AutoTune && settings.FacebookAccountId is Guid pageId)
        {
            var insights = await _insights.GetAsync(pageId, ct);
            if (insights.EnoughData) weight = i => PageInsightsService.Boost(insights, i.Topics, i.Region);
        }
        var pick = preselected ?? await PickStoryAsync(settings.Topic, keywords, region, allowOtherRegion: settings.RegionMode is not ("VN" or "World"), now, ct, weight: weight);
        if (pick == null)
        {
            run.Status = Failed;
            run.Message = $"Không còn tin hot nào thuộc chủ đề \"{NewsTopics.NameOf(settings.Topic)}\" chưa làm video trong 48 giờ qua.";
            return;
        }

        run.TrendingArticleId = pick.Value.Item.Id;
        run.ClusterId = pick.Value.ClusterId;
        run.ArticleUrl = pick.Value.Item.Url;
        run.ArticleTitle = Shorten(pick.Value.Item.Title, 500);
        run.SourceName = pick.Value.Item.SourceName;
        run.Region = pick.Value.Item.Region;
        run.HotScore = pick.Value.Item.HotScore;

        var project = await _projects.CreateProjectAsync(new CreateVideoProjectDto
        {
            Title = string.Empty,
            SourceUrl = pick.Value.Item.Url,
            Publisher = pick.Value.Item.SourceName,
            TargetDurationSeconds = settings.TargetDurationSeconds,
            AiProvider = settings.AiProvider,
            Voice = settings.Voice,
            VoiceSpeed = settings.VoiceSpeed,
            // The video carries this page's name and its topic's colours
            FacebookAccountId = run.FacebookAccountId
        }, ct);

        run.VideoProjectId = project.Id;
        run.Status = Producing;
        run.StartedAtUtc = now;
        run.Message = "Đang làm video.";
        await _context.SaveChangesAsync(ct);

        await _projects.EnqueueProjectProcessingAsync(project.Id, ct);
        _logger.LogInformation("Auto-post {Slot}: making video for \"{Title}\" ({Source}, topic {Topic}, score {Score:F0})",
            run.SlotKey, pick.Value.Item.Title, pick.Value.Item.SourceName, settings.Topic, pick.Value.Item.HotScore);
    }

    /// <summary>
    /// Hottest story of <paramref name="topic"/> in <paramref name="region"/> that no page has used: its link
    /// was never made into a video, and no other outlet's article about the same story (same cluster or same
    /// title) was used in the last few days — so two pages never post the same news.
    /// </summary>
    private async Task<(TrendingArticleDto Item, Guid? ClusterId)?> PickStoryAsync(
        string topic, List<string> keywords, string region, bool allowOtherRegion, DateTime now, CancellationToken ct,
        Func<TrendingArticleDto, bool>? accept = null, int[]? lookBackHours = null, Func<TrendingArticleDto, double>? weight = null)
    {
        var usedUrlList = await _context.VideoProjects.AsNoTracking().Select(p => p.SourceUrl).ToListAsync(ct);
        usedUrlList.AddRange(await _context.AutoPostRuns.AsNoTracking()
            .Where(r => r.ArticleUrl != null).Select(r => r.ArticleUrl!).ToListAsync(ct));
        var usedUrls = usedUrlList.Select(NormalizeUrl).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var since = now - RepeatStoryWindow;
        var usedStories = (await _context.AutoPostRuns.AsNoTracking()
                .Where(r => r.CreatedAtUtc >= since)
                .Select(r => new { r.ClusterId, r.TrendingArticleId })
                .ToListAsync(ct))
            .SelectMany(r => new[] { r.ClusterId, r.TrendingArticleId })
            .Where(id => id != null)
            .Select(id => id!.Value)
            .ToHashSet();
        // Stories already made into videos by hand (from the "Tin hot" page or a pasted link)
        var recentProjectUrls = await _context.VideoProjects.AsNoTracking()
            .Where(p => p.CreatedAtUtc >= since).Select(p => p.SourceUrl).ToListAsync(ct);
        foreach (var t in await _context.TrendingArticles.AsNoTracking()
                     .Where(t => recentProjectUrls.Contains(t.Url))
                     .Select(t => new { t.Id, t.ClusterId })
                     .ToListAsync(ct))
        {
            usedStories.Add(t.Id);
            if (t.ClusterId != null) usedStories.Add(t.ClusterId.Value);
        }
        var usedTitles = (await _context.VideoProjects.AsNoTracking()
                .Where(p => p.CreatedAtUtc >= since).Select(p => p.Title).ToListAsync(ct))
            .Select(NormalizeTitle).ToHashSet();
        // Same story in another language isn't clustered (clusters are per language): names such as
        // "Nvidia" + "Huang" shared with a story used in the last day mean it's the same news.
        var recentNames = (await _context.AutoPostRuns.AsNoTracking()
                .Where(r => r.CreatedAtUtc >= now.AddDays(-1) && r.ArticleTitle != null)
                .Select(r => r.ArticleTitle!)
                .ToListAsync(ct))
            .Select(ProperNames)
            .Where(n => n.Count >= 2)
            .ToList();

        var regions = allowOtherRegion ? new[] { region, region == "VN" ? "World" : "VN" } : new[] { region };
        foreach (var hours in lookBackHours ?? LookBackHours)
        {
            foreach (var r in regions)
            {
                var overview = await _trending.GetTrendingAsync(new TrendingQuery
                {
                    Region = r,
                    Hours = hours,
                    Limit = 120,
                    Topic = topic,
                    Keywords = keywords
                }, ct);
                var items = overview.Items
                    .Where(i => accept == null || accept(i))
                    .OrderByDescending(i => i.HotScore * (weight?.Invoke(i) ?? 1))
                    .ToList();
                if (items.Count == 0) continue;

                var ids = items.Select(i => i.Id).ToList();
                var clusters = await _context.TrendingArticles.AsNoTracking()
                    .Where(t => ids.Contains(t.Id))
                    .Select(t => new { t.Id, t.ClusterId })
                    .ToDictionaryAsync(t => t.Id, t => t.ClusterId, ct);

                foreach (var item in items)
                {
                    clusters.TryGetValue(item.Id, out var cluster);
                    if (usedUrls.Contains(NormalizeUrl(item.Url))) continue;
                    if (usedStories.Contains(item.Id) || (cluster != null && usedStories.Contains(cluster.Value))) continue;
                    if (usedTitles.Contains(NormalizeTitle(item.Title))) continue;
                    var names = ProperNames(item.Title);
                    if (names.Count >= 2 && recentNames.Any(n => n.Intersect(names).Count() >= 2)) continue;
                    return (item, cluster);
                }
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- breaking news

    public const int MaxBreakingPerDay = 5;
    /// <summary>A story is breaking news when this many outlets cover it within a few hours…</summary>
    public const int BreakingMinOutlets = 6;
    /// <summary>…or it matches a Google Trends search this big (and at least two outlets have it).</summary>
    public const int BreakingMinSearches = 20000;
    public static readonly TimeSpan BreakingMaxAge = TimeSpan.FromHours(3);
    private static readonly TimeSpan BreakingGap = TimeSpan.FromHours(2);

    public static bool IsBreakingNews(TrendingArticleDto item, DateTime nowUtc) =>
        nowUtc - item.PublishedAtUtc <= BreakingMaxAge
        && (item.CoverageCount >= BreakingMinOutlets || (item.TrendTraffic >= BreakingMinSearches && item.CoverageCount >= 2));

    /// <summary>
    /// A page may post breaking news now: 06:00–23:00, under its daily limit, 2 h after its last breaking
    /// post, and not right around a scheduled post (that post will pick the big story anyway).
    /// </summary>
    public static bool BreakingAllowed(DateTime nowUtc, IReadOnlyList<string> slots, IReadOnlyList<DateTime> todaysBreakingUtc, int perDay)
    {
        var hour = (nowUtc + VietnamOffset).Hour;
        if (hour < 6 || hour >= 23 || perDay <= 0) return false;
        if (todaysBreakingUtc.Count >= perDay) return false;
        if (todaysBreakingUtc.Any(t => nowUtc - t < BreakingGap)) return false;
        var today = VietnamDate(nowUtc);
        return !slots.Any(slot =>
        {
            var diff = SlotUtc(today, slot) - nowUtc;
            return diff < TimeSpan.FromMinutes(60) && diff > TimeSpan.FromMinutes(-30);
        });
    }

    private async Task StartBreakingNewsAsync(List<PageSchedule> pages, DateTime now, CancellationToken ct)
    {
        var dayStart = DayStartUtc(VietnamDate(now));
        foreach (var schedule in pages.Where(p => p.Settings.Enabled && p.Settings.BreakingEnabled))
        {
            var pageId = schedule.Page.Id;
            var todays = await _context.AutoPostRuns.AsNoTracking()
                .Where(r => r.FacebookAccountId == pageId && r.IsBreaking && r.ScheduledAtUtc >= dayStart)
                .Select(r => r.ScheduledAtUtc)
                .ToListAsync(ct);
            if (!BreakingAllowed(now, ParseSlots(schedule.Settings.TimeSlots), todays, schedule.Settings.BreakingPerDay)) continue;

            var settings = schedule.Settings;
            var keywords = NewsTopics.ParseKeywords(settings.TopicKeywords);
            if (settings.Topic == NewsTopics.Custom && keywords.Count == 0) continue;
            var region = settings.RegionMode == "World" ? "World" : "VN";
            var pick = await PickStoryAsync(settings.Topic, keywords, region, allowOtherRegion: settings.RegionMode is not ("VN" or "World"), now, ct,
                accept: i => IsBreakingNews(i, now), lookBackHours: new[] { (int)BreakingMaxAge.TotalHours });
            if (pick == null) continue;

            var run = new AutoPostRun
            {
                SlotKey = $"{VietnamDate(now):yyyy-MM-dd} breaking-{now + VietnamOffset:HHmmss} {pageId:N}",
                FacebookAccountId = pageId,
                ScheduledAtUtc = now,
                IsBreaking = true,
                Status = Producing,
                StartedAtUtc = now
            };
            _context.AutoPostRuns.Add(run);
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Breaking news for page {Page}: {Title} ({Outlets} outlets, trends {Trends})",
                schedule.Page.PageName, pick.Value.Item.Title, pick.Value.Item.CoverageCount, pick.Value.Item.TrendTraffic);

            await StartProductionAsync(run, settings, region, now, ct, pick);
            if (run.Status == Producing)
                run.Message = $"Tin nóng ({pick.Value.Item.CoverageCount} báo cùng đưa): đang làm video, xong là đăng ngay.";
            run.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            return; // one video at a time
        }
    }

    private async Task PublishAsync(AutoPostRun run, CancellationToken ct)
    {
        var projectId = run.VideoProjectId!.Value;
        run.Status = Publishing;
        run.Message = "Đang đăng lên Facebook.";
        run.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        try
        {
            await _review.SubmitDecisionAsync(projectId, new SubmitReviewDecisionRequestDto
            {
                Decision = "Approved",
                ReviewedBy = "Tự động đăng",
                FeedbackNotes = run.IsManual ? "Tự động duyệt (Đăng ngay)" : $"Tự động duyệt theo lịch {VietnamTime(run.ScheduledAtUtc)}"
            }, ct);
        }
        catch (InvalidOperationException ex)
        {
            // Serious quality problems still block posting — leave the video for the editor.
            run.Status = Failed;
            run.Message = $"Video chưa đạt kiểm tra chất lượng nên không tự đăng: {Shorten(ex.Message, 300)} Vào trang Video để xem và duyệt tay.";
            return;
        }

        // AI editorial pass: the headline must match the article; professional caption + first comment.
        // Normally done at the "Kiểm chứng" step before the voice-over — reused here (no second AI call).
        EditorialPassDto? pass = null;
        string? passNote = null;
        try
        {
            var stored = await _context.VideoProjects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.EditorialPassJson).FirstOrDefaultAsync(ct);
            pass = string.IsNullOrWhiteSpace(stored) ? null : System.Text.Json.JsonSerializer.Deserialize<EditorialPassDto>(stored);
            pass ??= await _editorial.RunAsync(projectId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // AI unavailable: post with the plain caption rather than miss the slot, and say so
            _logger.LogWarning(ex, "Editorial pass for {Slot} failed", run.SlotKey);
            passNote = $" (Không kiểm tra được tiêu đề và viết caption bằng AI: {Shorten(ex.Message, 160)})";
        }
        if (pass is { Consistent: false })
        {
            run.Status = Failed;
            run.Message = $"Không tự đăng vì tiêu đề/nội dung video chưa khớp bài gốc: {Shorten(string.Join("; ", pass.Issues), 400)} Vào trang Video để xem và quyết định.";
            return;
        }

        // Each page posts with its own token: the request names the page explicitly
        var postToInstagram = await _context.AutoPostSettings.AsNoTracking()
            .Where(s => s.FacebookAccountId == run.FacebookAccountId)
            .Select(s => s.InstagramEnabled)
            .FirstOrDefaultAsync(ct);
        var result = await _facebook.PublishReelAsync(projectId, new PublishReelRequestDto
        {
            FacebookAccountId = run.FacebookAccountId,
            CustomCaption = pass?.Caption,
            FirstComment = pass?.FirstComment,
            PostToInstagram = postToInstagram
        }, ct);
        if (string.Equals(result.UploadStatus, "Published", StringComparison.OrdinalIgnoreCase))
        {
            run.Status = Published;
            run.FacebookPostUrl = result.PostUrl;
            run.PublishedAtUtc = result.PublishedAtUtc ?? DateTime.UtcNow;
            run.Message = "Đã đăng lên Facebook."
                          + (result.FirstCommentId != null ? " Đã bình luận đầu tiên." : "")
                          + (result.FirstCommentError != null ? $" Chưa bình luận được: {Shorten(result.FirstCommentError, 200)}" : "")
                          + (result.InstagramMediaId != null ? " Đã đăng cả Instagram." : "")
                          + (result.InstagramError != null ? $" Instagram lỗi: {Shorten(result.InstagramError, 200)}" : "")
                          + (passNote ?? "");
            _logger.LogInformation("Auto-post {Slot} published: {Url}", run.SlotKey, result.PostUrl);
        }
        else
        {
            run.Status = Failed;
            run.Message = $"Đăng Facebook thất bại: {result.ErrorMessage ?? result.UploadStatus}. Video vẫn ở trang Video, có thể đăng lại tay.";
        }
    }

    // ---------------------------------------------------------------- small helpers

    private static string GateReasons(string? gateJson)
    {
        if (string.IsNullOrWhiteSpace(gateJson)) return "chưa qua kiểm chứng.";
        try
        {
            var gate = System.Text.Json.JsonSerializer.Deserialize<QualityGateDto>(gateJson);
            return gate == null || gate.Reasons.Count == 0 ? "chưa qua kiểm chứng." : Shorten(string.Join(" ", gate.Reasons), 300);
        }
        catch (System.Text.Json.JsonException)
        {
            return "chưa qua kiểm chứng.";
        }
    }

    private static string StepLabel(VideoProjectStatus status) => status switch
    {
        VideoProjectStatus.Pending => "đang chờ",
        VideoProjectStatus.Extracting or VideoProjectStatus.Extracted => "đọc bài báo",
        VideoProjectStatus.Analyzing or VideoProjectStatus.Scripting => "viết kịch bản",
        VideoProjectStatus.PlanningScenes => "chia cảnh",
        VideoProjectStatus.GeneratingAudio => "đọc giọng",
        VideoProjectStatus.Rendering => "dựng video",
        VideoProjectStatus.QualityChecking => "kiểm tra chất lượng",
        _ => status.ToString()
    };

    public static string NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var u = url.Trim();
        var hash = u.IndexOf('#');
        if (hash >= 0) u = u[..hash];
        if (Uri.TryCreate(u, UriKind.Absolute, out var parsed))
        {
            // Tracking parameters don't make a different article
            var query = string.Join("&", parsed.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
                            && !p.StartsWith("fbclid", StringComparison.OrdinalIgnoreCase)));
            var host = parsed.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? parsed.Host[4..] : parsed.Host;
            u = host + parsed.AbsolutePath.TrimEnd('/') + (query.Length > 0 ? "?" + query : string.Empty);
        }
        return u.ToLowerInvariant();
    }

    private static readonly System.Text.RegularExpressions.Regex NameRegex = new(@"(?<![\p{L}\p{N}])[A-Z][a-zA-Z]{3,}(?![\p{L}\p{N}])",System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Latin-script proper names in a title (e.g. "Nvidia", "Huang", "Trump"), ignoring the first word and
    /// accent-free Vietnamese syllables that are capitalised in headlines.
    /// </summary>
    public static HashSet<string> ProperNames(string title)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var body = string.Join(' ', words.Skip(1));
        return NameRegex.Matches(body)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => !CommonCapitalised.Contains(w))
            .ToHashSet();
    }

    private static readonly HashSet<string> CommonCapitalised = new(StringComparer.OrdinalIgnoreCase)
    {
        // Vietnamese syllables without accents that appear capitalised in names / places
        "long", "minh", "linh", "lan", "thanh", "trang", "giang", "binh", "hoang", "quang", "phong", "tien",
        "anh", "nam", "hung", "dung", "tuan", "khanh", "ninh", "kien", "lam", "son", "chi", "tam", "tin",
        // English words often capitalised in headlines
        "news", "live", "update", "says", "after", "with", "from", "over", "into", "what", "will", "this",
        "that", "more", "than", "year", "world", "first", "people", "report", "government", "president"
    };

    private static string NormalizeTitle(string? title) =>
        new string((title ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string Shorten(string? text, int max) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Length <= max ? text : text[..(max - 1)] + "…";
}
