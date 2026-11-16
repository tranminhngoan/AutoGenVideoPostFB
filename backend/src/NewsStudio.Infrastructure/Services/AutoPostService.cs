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

namespace NewsStudio.Infrastructure.Services;

/// <summary>
/// "Tự động đăng". A recurring job (every 2 minutes) does, for each daily time slot (Vietnam time):
/// <list type="number">
/// <item>LeadMinutes before the slot: pick the hottest story not turned into a video yet (no repeats, also
/// not the same story from another outlet), alternating Vietnam / international news, and start the normal
/// video pipeline for its link.</item>
/// <item>When the video is ready and the slot time has come: approve it (the quality check still blocks
/// videos with serious problems) and post it to the Facebook Page.</item>
/// </list>
/// A story whose video fails is replaced by the next hottest one (once), as long as the slot isn't long past.
/// </summary>
public class AutoPostService : IAutoPostService
{
    public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    public const int MaxPostsPerDay = 12;

    public const string Producing = "Producing";
    public const string WaitingSlot = "WaitingSlot";
    public const string Publishing = "Publishing";
    public const string Published = "Published";
    public const string Failed = "Failed";
    public const string Skipped = "Skipped";

    private const int MaxAttempts = 2;
    /// <summary>A slot can still start this late (e.g. the server was restarting at the slot time).</summary>
    private static readonly TimeSpan LateWindow = TimeSpan.FromMinutes(90);
    /// <summary>A replacement story is only tried while the slot is less than this far in the past.</summary>
    private static readonly TimeSpan RetryWindow = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan ProduceTimeout = TimeSpan.FromMinutes(75);
    private static readonly TimeSpan PublishingStuckAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RepeatStoryWindow = TimeSpan.FromDays(3);

    private static readonly SemaphoreSlim TickLock = new(1, 1);

    private readonly IApplicationDbContext _context;
    private readonly ITrendingNewsService _trending;
    private readonly IVideoProjectService _projects;
    private readonly IVideoReviewService _review;
    private readonly IFacebookPublishingService _facebook;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AutoPostService> _logger;

    public AutoPostService(
        IApplicationDbContext context,
        ITrendingNewsService trending,
        IVideoProjectService projects,
        IVideoReviewService review,
        IFacebookPublishingService facebook,
        IConfiguration configuration,
        ILogger<AutoPostService> logger)
    {
        _context = context;
        _trending = trending;
        _projects = projects;
        _review = review;
        _facebook = facebook;
        _configuration = configuration;
        _logger = logger;
    }

    // ---------------------------------------------------------------- schedule helpers

    /// <summary>
    /// Suggested posting times (Vietnam) for a number of posts per day, built around Facebook's busy hours
    /// in Vietnam: breakfast/commute, lunch break, after work, evening prime time and before bed.
    /// </summary>
    public static List<string> SuggestSlots(int count)
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
        if (preset.Length > 0) return preset.ToList();

        // More than 8: spread evenly between 06:30 and 22:30
        var start = TimeSpan.FromHours(6.5);
        var step = (TimeSpan.FromHours(22.5) - start) / (count - 1);
        return Enumerable.Range(0, count)
            .Select(i => start + TimeSpan.FromMinutes(Math.Round((step * i).TotalMinutes / 5) * 5))
            .Select(t => t.ToString(@"hh\:mm", CultureInfo.InvariantCulture))
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

    private static DateTime VietnamDate(DateTime utc) => (utc + VietnamOffset).Date;

    private static DateTime SlotUtc(DateTime vietnamDate, string time) =>
        DateTime.SpecifyKind(vietnamDate + TimeSpan.ParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture) - VietnamOffset, DateTimeKind.Utc);

    private static string SlotKey(DateTime vietnamDate, string time) =>
        $"{vietnamDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {time}";

    private static string VietnamTime(DateTime utc) => (utc + VietnamOffset).ToString("HH:mm", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- settings / overview

    private async Task<AutoPostSettings> LoadSettingsAsync(CancellationToken ct)
    {
        var settings = await _context.AutoPostSettings.OrderBy(s => s.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (settings != null) return settings;

        settings = new AutoPostSettings { TimeSlots = string.Join(",", SuggestSlots(5)) };
        _context.AutoPostSettings.Add(settings);
        await _context.SaveChangesAsync(ct);
        return settings;
    }

    private static AutoPostSettingsDto ToDto(AutoPostSettings s) => new()
    {
        Enabled = s.Enabled,
        PostsPerDay = s.PostsPerDay,
        TimeSlots = ParseSlots(s.TimeSlots),
        RegionMode = s.RegionMode,
        Voice = s.Voice,
        VoiceSpeed = s.VoiceSpeed,
        AiProvider = s.AiProvider,
        TargetDurationSeconds = s.TargetDurationSeconds,
        FacebookAccountId = s.FacebookAccountId,
        LeadMinutes = s.LeadMinutes
    };

    public async Task<AutoPostSettingsDto> UpdateSettingsAsync(AutoPostSettingsDto dto, CancellationToken cancellationToken = default)
    {
        var settings = await LoadSettingsAsync(cancellationToken);

        var count = Math.Clamp(dto.PostsPerDay, 1, MaxPostsPerDay);
        var slots = ParseSlots(string.Join(",", dto.TimeSlots ?? new List<string>()));
        if (slots.Count != count) slots = SuggestSlots(count);

        settings.Enabled = dto.Enabled;
        settings.PostsPerDay = count;
        settings.TimeSlots = string.Join(",", slots);
        settings.RegionMode = dto.RegionMode is "VN" or "World" ? dto.RegionMode : "Mixed";
        settings.Voice = string.IsNullOrWhiteSpace(dto.Voice) ? null : dto.Voice.Trim();
        settings.VoiceSpeed = string.IsNullOrWhiteSpace(dto.VoiceSpeed) ? null : dto.VoiceSpeed.Trim();
        settings.AiProvider = string.IsNullOrWhiteSpace(dto.AiProvider) ? null : dto.AiProvider.Trim();
        settings.TargetDurationSeconds = Math.Clamp(dto.TargetDurationSeconds, 30, 150);
        settings.FacebookAccountId = dto.FacebookAccountId;
        settings.LeadMinutes = Math.Clamp(dto.LeadMinutes, 15, 120);
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Auto-post settings saved: enabled={Enabled}, {Count}/day at {Slots}, region {Region}",
            settings.Enabled, settings.PostsPerDay, settings.TimeSlots, settings.RegionMode);
        return ToDto(settings);
    }

    public async Task<AutoPostOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var today = VietnamDate(now);
        var dayStartUtc = DateTime.SpecifyKind(today - VietnamOffset, DateTimeKind.Utc);
        var since = dayStartUtc.AddDays(-7);

        var runs = await _context.AutoPostRuns.AsNoTracking()
            .Where(r => r.ScheduledAtUtc >= since)
            .OrderByDescending(r => r.ScheduledAtUtc)
            .Take(80)
            .ToListAsync(cancellationToken);
        var runDtos = await MapRunsAsync(runs, cancellationToken);
        var byKey = runDtos.ToDictionary(r => r.SlotKey);

        var slots = ParseSlots(settings.TimeSlots);
        var todaySlots = slots.Select((time, i) =>
        {
            var at = SlotUtc(today, time);
            byKey.TryGetValue(SlotKey(today, time), out var run);
            return new AutoPostSlotDto
            {
                Time = time,
                ScheduledAtUtc = at,
                PlannedRegion = run?.Region ?? PlannedRegion(settings.RegionMode, i),
                State = run?.Status ?? (now > at + LateWindow ? "Missed" : "Upcoming"),
                Run = run
            };
        }).ToList();

        var facebookConnected = await _context.FacebookAccounts.AnyAsync(a => a.IsActive, cancellationToken);
        var warnings = new List<string>();
        if (!facebookConnected)
            warnings.Add("Chưa kết nối Page Facebook. Vào Cài đặt → Facebook để kết nối, nếu không video sẽ không đăng được.");
        if (!GeminiKeys.HasAny(_configuration) && !SimulationMode.IsEnabled(_configuration))
            warnings.Add("Máy chủ chưa có API key Gemini nên không viết được kịch bản.");

        DateTime? next = todaySlots.Where(s => s.ScheduledAtUtc > now && s.Run == null).Select(s => (DateTime?)s.ScheduledAtUtc).FirstOrDefault();
        if (next == null && slots.Count > 0) next = SlotUtc(today.AddDays(1), slots[0]);

        return new AutoPostOverviewDto
        {
            Settings = ToDto(settings),
            Today = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TodaySlots = todaySlots,
            RecentRuns = runDtos,
            PublishedToday = runDtos.Count(r => r.Status == Published && r.ScheduledAtUtc >= dayStartUtc),
            NextSlotUtc = settings.Enabled ? next : null,
            FacebookConnected = facebookConnected,
            Warnings = warnings
        };
    }

    private async Task<List<AutoPostRunDto>> MapRunsAsync(List<AutoPostRun> runs, CancellationToken ct)
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
            ScheduledAtUtc = r.ScheduledAtUtc,
            SlotTime = VietnamTime(r.ScheduledAtUtc),
            SlotDate = VietnamDate(r.ScheduledAtUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IsManual = r.IsManual,
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

    public async Task<AutoPostRunDto> RunNowAsync(CancellationToken cancellationToken = default)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var today = VietnamDate(now);
        var dayStartUtc = DateTime.SpecifyKind(today - VietnamOffset, DateTimeKind.Utc);

        // "Mixed": take the region posted less today
        var region = settings.RegionMode;
        if (region is not ("VN" or "World"))
        {
            var todayRegions = await _context.AutoPostRuns
                .Where(r => r.ScheduledAtUtc >= dayStartUtc && r.Region != null && r.Status != Failed && r.Status != Skipped)
                .Select(r => r.Region!)
                .ToListAsync(cancellationToken);
            region = todayRegions.Count(r => r == "World") < todayRegions.Count(r => r == "VN") ? "World" : "VN";
        }

        var run = new AutoPostRun
        {
            SlotKey = $"{today:yyyy-MM-dd} now-{(now + VietnamOffset):HHmmss}",
            ScheduledAtUtc = now,
            IsManual = true,
            Status = Producing,
            StartedAtUtc = now
        };
        _context.AutoPostRuns.Add(run);
        await _context.SaveChangesAsync(cancellationToken);

        await StartProductionAsync(run, settings, region, now, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return (await MapRunsAsync(new List<AutoPostRun> { run }, cancellationToken))[0];
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
            var settings = await LoadSettingsAsync(cancellationToken);
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
                    await AdvanceAsync(run, settings, now, cancellationToken);
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

            if (!settings.Enabled) return;

            // 2. Start the next due slot (one per tick, and not while another video is being made unless the slot is close)
            var today = VietnamDate(now);
            var slots = ParseSlots(settings.TimeSlots);
            var lead = TimeSpan.FromMinutes(settings.LeadMinutes);
            for (var i = 0; i < slots.Count; i++)
            {
                var at = SlotUtc(today, slots[i]);
                if (now < at - lead || now > at + LateWindow) continue;

                var key = SlotKey(today, slots[i]);
                if (await _context.AutoPostRuns.AnyAsync(r => r.SlotKey == key, cancellationToken)) continue;
                if (active.Any(r => r.Status == Producing) && now < at - TimeSpan.FromMinutes(10)) break;

                var run = new AutoPostRun { SlotKey = key, ScheduledAtUtc = at, Status = Producing, StartedAtUtc = now };
                _context.AutoPostRuns.Add(run);
                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Auto-post slot {Slot} started", key);

                await StartProductionAsync(run, settings, PlannedRegion(settings.RegionMode, i), now, cancellationToken);
                run.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                break;
            }
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

    private async Task AdvanceAsync(AutoPostRun run, AutoPostSettings settings, DateTime now, CancellationToken ct)
    {
        if (!settings.Enabled && !run.IsManual && run.Status is Producing or WaitingSlot)
        {
            run.Status = Skipped;
            run.Message = "Đã tắt tự động đăng. Video (nếu đã làm) vẫn nằm ở trang Video để duyệt tay.";
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
            .Select(p => new { p.Status, p.ErrorMessage })
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
                    case VideoProjectStatus.AwaitingApproval:
                        if (now >= run.ScheduledAtUtc)
                        {
                            await PublishAsync(run, settings, ct);
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
                        await RetryOrFailAsync(run, settings, now, project.ErrorMessage ?? "Dựng video thất bại", ct);
                        break;
                    default:
                        if (run.StartedAtUtc != null && now - run.StartedAtUtc > ProduceTimeout)
                        {
                            await RetryOrFailAsync(run, settings, now, "Làm video quá lâu, có thể bị kẹt", ct);
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
                if (now >= run.ScheduledAtUtc) await PublishAsync(run, settings, ct);
                break;

            case Publishing:
                // Only reached when a previous tick died mid-upload; publishing is idempotent per video.
                if (now - (run.UpdatedAtUtc ?? run.CreatedAtUtc) > PublishingStuckAfter) await PublishAsync(run, settings, ct);
                break;
        }
    }

    private async Task RetryOrFailAsync(AutoPostRun run, AutoPostSettings settings, DateTime now, string reason, CancellationToken ct)
    {
        if (run.Attempts < MaxAttempts && now < run.ScheduledAtUtc + RetryWindow)
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

    private async Task StartProductionAsync(AutoPostRun run, AutoPostSettings settings, string region, DateTime now, CancellationToken ct)
    {
        run.Attempts++;

        if (!SimulationMode.IsEnabled(_configuration) && !await _context.FacebookAccounts.AnyAsync(a => a.IsActive, ct))
        {
            run.Status = Failed;
            run.Message = "Chưa kết nối Page Facebook nên không tự đăng được. Vào Cài đặt → Facebook để kết nối.";
            return;
        }

        var pick = await PickStoryAsync(region, allowOtherRegion: settings.RegionMode is not ("VN" or "World"), now, ct);
        if (pick == null)
        {
            run.Status = Failed;
            run.Message = "Không còn tin hot nào chưa làm video trong 24 giờ qua.";
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
            VoiceSpeed = settings.VoiceSpeed
        }, ct);

        run.VideoProjectId = project.Id;
        run.Status = Producing;
        run.StartedAtUtc = now;
        run.Message = "Đang làm video.";
        await _context.SaveChangesAsync(ct);

        await _projects.EnqueueProjectProcessingAsync(project.Id, ct);
        _logger.LogInformation("Auto-post {Slot}: making video for \"{Title}\" ({Source}, score {Score:F0})",
            run.SlotKey, pick.Value.Item.Title, pick.Value.Item.SourceName, pick.Value.Item.HotScore);
    }

    /// <summary>
    /// Hottest story in <paramref name="region"/> that has no video yet: its link was never used, and no
    /// other outlet's article about the same story (same cluster) was used in the last few days.
    /// </summary>
    private async Task<(TrendingArticleDto Item, Guid? ClusterId)?> PickStoryAsync(string region, bool allowOtherRegion, DateTime now, CancellationToken ct)
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

        var regions = allowOtherRegion ? new[] { region, region == "VN" ? "World" : "VN" } : new[] { region };
        foreach (var r in regions)
        {
            foreach (var hours in new[] { 12, 24 })
            {
                var overview = await _trending.GetTrendingAsync(new TrendingQuery { Region = r, Hours = hours, Limit = 80 }, ct);
                var items = overview.Items.OrderByDescending(i => i.HotScore).ToList();
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
                    return (item, cluster);
                }
            }
        }
        return null;
    }

    private async Task PublishAsync(AutoPostRun run, AutoPostSettings settings, CancellationToken ct)
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

        var result = await _facebook.PublishReelAsync(projectId, new PublishReelRequestDto { FacebookAccountId = settings.FacebookAccountId }, ct);
        if (string.Equals(result.UploadStatus, "Published", StringComparison.OrdinalIgnoreCase))
        {
            run.Status = Published;
            run.FacebookPostUrl = result.PostUrl;
            run.PublishedAtUtc = result.PublishedAtUtc ?? DateTime.UtcNow;
            run.Message = "Đã đăng lên Facebook.";
            _logger.LogInformation("Auto-post {Slot} published: {Url}", run.SlotKey, result.PostUrl);
        }
        else
        {
            run.Status = Failed;
            run.Message = $"Đăng Facebook thất bại: {result.ErrorMessage ?? result.UploadStatus}. Video vẫn ở trang Video, có thể đăng lại tay.";
        }
    }

    // ---------------------------------------------------------------- small helpers

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

    private static string NormalizeTitle(string? title) =>
        new string((title ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string Shorten(string? text, int max) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Length <= max ? text : text[..(max - 1)] + "…";
}
