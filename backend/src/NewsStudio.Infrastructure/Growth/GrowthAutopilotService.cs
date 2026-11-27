using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.AiStudio;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Infrastructure.Growth;

/// <summary>
/// "Chế độ tự động": once a day (from 05:00 Vietnam time) the app prepares and schedules by itself, for each Page that
/// has it on:
/// <list type="bullet">
/// <item>a wall post from an unused drama script — an AI picture of the script's key moment (Page watermark) and an AI
/// text about it; a script is never used twice on the Page; when none is left the owner is told;</item>
/// <item>the newest finished, not yet posted video as a Reel (videos are never generated here — each costs ~2 USD);</item>
/// <item>Stories: the liveliest ~45 s of a Page video (by loudness), and the day's script picture.</item>
/// </list>
/// Everything goes into the Page's running campaign (a 7-day "Tự động" campaign is started when there is none) with
/// fixed idempotency keys per day and slot, so two workers can never make the same item twice; posting is done by the
/// normal scheduler. A paused campaign pauses the autopilot too.
/// </summary>
public class GrowthAutopilotService
{
    public const int PlanFromHour = 5;
    public const int MaxAttemptsPerDay = 3;
    public const int LowScriptsWarning = 7;

    public static readonly Dictionary<string, string> DefaultSlots = new()
    {
        ["post"] = "11:30", ["reel"] = "19:30", ["story1"] = "07:30", ["story2"] = "21:00", ["story3"] = "13:00", ["story4"] = "17:00"
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly GrowthCampaignService _campaigns;
    private readonly GrowthContentService _content;
    private readonly GrowthInsightsService _insights;
    private readonly GrowthAiService _ai;
    private readonly GrowthMedia _media;
    private readonly AiStudioMedia _probe;
    private readonly StudioService _studio;
    private readonly StudioMedia _studioMedia;
    private readonly AiStudioService _aiStudio;
    private readonly GrowthNewsService _news;
    private readonly ILogger<GrowthAutopilotService> _logger;
    /// <summary>Videos not assigned to any Page may be used only when this is the one script (nostalgia) Page.</summary>
    private bool _allowUnassigned;

    public GrowthAutopilotService(IApplicationDbContext db, GrowthCampaignService campaigns, GrowthContentService content, GrowthInsightsService insights,
        GrowthAiService ai, GrowthMedia media, AiStudioMedia probe, StudioService studio, StudioMedia studioMedia, AiStudioService aiStudio,
        GrowthNewsService news, ILogger<GrowthAutopilotService> logger)
    {
        _news = news;
        _db = db;
        _campaigns = campaigns;
        _content = content;
        _insights = insights;
        _ai = ai;
        _media = media;
        _probe = probe;
        _studio = studio;
        _studioMedia = studioMedia;
        _aiStudio = aiStudio;
        _logger = logger;
    }

    /// <summary>Current time (replaceable in tests).</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    public static string Key(Guid pageId, DateOnly day, string slot) => $"auto:{pageId:N}:{day:yyyyMMdd}:{slot}";

    // ═══════════════════════════════ scripts ═══════════════════════════════

    public async Task<int> ImportScriptsAsync(List<GrowthScriptImportDto> items, CancellationToken ct)
    {
        if (items.Count > 100) throw new ArgumentException("Tối đa 100 kịch bản mỗi lần.");
        var codes = items.Select(i => (i.Code ?? "").Trim()).Where(c => c.Length > 0).ToList();
        var existing = await _db.GrowthScripts.Where(s => codes.Contains(s.Code)).ToDictionaryAsync(s => s.Code, ct);
        var added = 0;
        foreach (var i in items)
        {
            var code = (i.Code ?? "").Trim();
            if (code.Length == 0 || code.Length > 100 || string.IsNullOrWhiteSpace(i.Title)) continue;
            if (!existing.TryGetValue(code, out var s))
            {
                s = new GrowthScript { Code = code };
                _db.GrowthScripts.Add(s);
                existing[code] = s;
                added++;
            }
            s.Title = Cut(i.Title, 300)!;
            s.Category = Cut(i.Category, 60);
            s.CategoryName = Cut(i.CategoryName, 200);
            s.Mood = Cut(i.Mood, 60);
            s.Era = Cut(i.Era, 10);
            s.Region = Cut(i.Region, 40);
            s.Synopsis = Cut(i.Synopsis, 4000) ?? "";
            s.Hook = Cut(i.Hook, 2000) ?? "";
            s.EndingNote = Cut(i.EndingNote, 2000) ?? "";
            s.ScenesJson = JsonSerializer.Serialize((i.Scenes ?? new()).Take(12).Select(x => new
            {
                title = Cut(x.Title, 300), visualVi = Cut(x.VisualVi, 2000), aiPromptEn = Cut(x.AiPromptEn, 2000), voiceover = Cut(x.Voiceover, 2000)
            }), Json);
            s.Source = i.Source is "custom" or "ai" ? i.Source : "library";
        }
        await _db.SaveChangesAsync(ct);
        return added;
    }

    private static string? Cut(string? s, int max) => s == null ? null : (s = s.Trim()).Length > max ? s[..max] : s;

    public async Task<GrowthScriptStatsDto> ScriptStatsAsync(Guid? pageId, CancellationToken ct)
    {
        var all = await _db.GrowthScripts.AsNoTracking().Select(s => new { s.Id, s.Code, s.CategoryName }).ToListAsync(ct);
        var used = pageId is { } pid
            ? await _db.GrowthScriptUsages.AsNoTracking().Where(u => u.FacebookAccountId == pid).Select(u => u.ScriptId).ToListAsync(ct)
            : new List<Guid>();
        var usedSet = used.ToHashSet();
        return new GrowthScriptStatsDto
        {
            Total = all.Count, Used = usedSet.Count, Remaining = all.Count(s => !usedSet.Contains(s.Id)),
            Codes = all.Select(s => s.Code).ToList(),
            RemainingByCategory = all.Where(s => !usedSet.Contains(s.Id)).GroupBy(s => s.CategoryName ?? "Khác")
                .Select(g => new GrowthCountDto { Label = g.Key, Value = g.Count() }).OrderBy(g => g.Label).ToList()
        };
    }

    /// <summary>The next unused script of the Page: topics take turns (least used topic first, never the same topic twice in a row).</summary>
    public async Task<GrowthScript?> NextScriptAsync(Guid pageId, CancellationToken ct)
    {
        var usages = await _db.GrowthScriptUsages.AsNoTracking().Where(u => u.FacebookAccountId == pageId)
            .Select(u => new { u.ScriptId, u.CreatedAtUtc, u.Script!.Category }).ToListAsync(ct);
        var used = usages.Select(u => u.ScriptId).ToHashSet();
        var unused = await _db.GrowthScripts.AsNoTracking().Where(s => !used.Contains(s.Id)).Select(s => new { s.Id, s.Code, s.Category }).ToListAsync(ct);
        if (unused.Count == 0) return null;
        var last = usages.OrderByDescending(u => u.CreatedAtUtc).FirstOrDefault()?.Category;
        var usedPerCategory = usages.GroupBy(u => u.Category ?? "").ToDictionary(g => g.Key, g => g.Count());
        var pick = unused
            .OrderBy(s => s.Category == last && unused.Any(o => o.Category != last) ? 1 : 0)
            .ThenBy(s => usedPerCategory.GetValueOrDefault(s.Category ?? ""))
            .ThenBy(s => s.Code, StringComparer.Ordinal)
            .First();
        return await _db.GrowthScripts.AsNoTracking().FirstAsync(s => s.Id == pick.Id, ct);
    }

    // ═══════════════════════════════ settings & status ═══════════════════════════════

    private sealed class DayState
    {
        public string Date { get; set; } = "";
        public Dictionary<string, int> Attempts { get; set; } = new();
        public List<string> Skipped { get; set; } = new();
    }

    private static List<GrowthNoticeDto> Notices(GrowthAutopilot a)
    {
        try { return JsonSerializer.Deserialize<List<GrowthNoticeDto>>(a.NoticesJson, Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public async Task<GrowthAutopilotDto> GetAsync(Guid pageId, CancellationToken ct)
    {
        if (!await _db.FacebookAccounts.AnyAsync(a => a.Id == pageId, ct)) throw new KeyNotFoundException("Không tìm thấy Page.");
        var a = await _db.GrowthAutopilots.AsNoTracking().FirstOrDefaultAsync(x => x.FacebookAccountId == pageId, ct)
                ?? new GrowthAutopilot { FacebookAccountId = pageId };
        var today = GrowthTime.Today(Clock());
        var prefix = $"auto:{pageId:N}:{today:yyyyMMdd}:";
        var items = await _db.GrowthScheduledContents.AsNoTracking().Where(x => x.IdempotencyKey.StartsWith(prefix)).OrderBy(x => x.ScheduledAtUtc).ToListAsync(ct);
        var campaign = await _db.GrowthCampaigns.AsNoTracking()
            .Where(c => c.FacebookAccountId == pageId && (c.Status == GrowthStatus.Running || c.Status == GrowthStatus.Paused)).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        var news = await _news.NewsSettingsAsync(pageId, ct);
        var mode = GrowthNewsService.EffectiveMode(a.ContentMode, news != null);
        var dto = new GrowthAutopilotDto
        {
            ContentMode = a.ContentMode, EffectiveMode = mode,
            NewsTopic = news?.Topic, NewsTopicName = news == null ? null : NewsStudio.Infrastructure.Trending.NewsTopics.NameOf(news.Topic),
            FacebookAccountId = pageId, Enabled = a.Enabled, ScriptPost = a.ScriptPost, DailyVideo = a.DailyVideo, StoriesPerDay = a.StoriesPerDay,
            LastRunAtUtc = a.LastRunAtUtc, Notices = Notices(a), Scripts = await ScriptStatsAsync(pageId, ct), CampaignId = campaign,
            Today = await _content.ToDtosAsync(items, ct), Slots = new Dictionary<string, string>(DefaultSlots)
        };
        dto.Scripts.Codes = new(); // not needed by the page here
        return dto;
    }

    public async Task<GrowthAutopilotDto> SetAsync(GrowthAutopilotInputDto input, CancellationToken ct)
    {
        var page = await _db.FacebookAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == input.FacebookAccountId, ct)
                   ?? throw new KeyNotFoundException("Không tìm thấy Page.");
        var a = await _db.GrowthAutopilots.FirstOrDefaultAsync(x => x.FacebookAccountId == page.Id, ct);
        if (a == null)
        {
            a = new GrowthAutopilot { FacebookAccountId = page.Id };
            _db.GrowthAutopilots.Add(a);
        }
        if (input.Enabled == true && (!page.IsActive || string.IsNullOrWhiteSpace(page.EncryptedAccessToken)))
            throw new InvalidOperationException("Page chưa kết nối — kết nối lại ở Cài đặt → Facebook trước khi bật chế độ tự động.");
        if (input.Enabled is { } e) a.Enabled = e;
        if (input.ContentMode != null)
            a.ContentMode = input.ContentMode is "Auto" or "Scripts" or "News" ? input.ContentMode : throw new ArgumentException("Chế độ nội dung là Auto, Scripts hoặc News.");
        if (input.ScriptPost is { } sp) a.ScriptPost = sp;
        if (input.DailyVideo is { } dv) a.DailyVideo = dv;
        if (input.StoriesPerDay is { } n) a.StoriesPerDay = n is < 0 or > 4 ? throw new ArgumentException("Story mỗi ngày từ 0 đến 4.") : n;
        await _db.SaveChangesAsync(ct);
        return await GetAsync(page.Id, ct);
    }

    // ═══════════════════════════════ the daily run ═══════════════════════════════

    /// <summary>Recurring (every 10 minutes): plans today's content for every Page with the autopilot on.</summary>
    [Hangfire.AutomaticRetry(Attempts = 0)]
    [Hangfire.DisableConcurrentExecution(900)]
    public async Task TickAsync(CancellationToken ct)
    {
        if (GrowthTime.ToLocal(Clock()).Hour < PlanFromHour) return;
        var pages = await _db.GrowthAutopilots.AsNoTracking().Where(a => a.Enabled).Select(a => a.FacebookAccountId).ToListAsync(ct);
        foreach (var page in pages)
        {
            try { await RunAsync(page, force: false, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Growth autopilot for {Page} failed: {Message}", page, GrowthGraphClient.Redact(ex.Message));
            }
        }
    }

    /// <summary>Plans today's items for one Page (idempotent: what already exists today is left alone).</summary>
    public async Task<GrowthAutopilotDto> RunAsync(Guid pageId, bool force, CancellationToken ct)
    {
        var a = await _db.GrowthAutopilots.FirstOrDefaultAsync(x => x.FacebookAccountId == pageId, ct) ?? throw new KeyNotFoundException("Chưa bật chế độ tự động cho Page này.");
        if (!a.Enabled && !force) return await GetAsync(pageId, ct);
        var notices = new List<GrowthNoticeDto>();
        var now = Clock();
        var today = GrowthTime.Today(now);
        void Notice(string level, string code, string message) => notices.Add(new GrowthNoticeDto { Level = level, Code = code, Message = message, AtUtc = now });

        var state = ReadState(a, today);
        var newsSettings = await _news.NewsSettingsAsync(pageId, ct);
        var mode = GrowthNewsService.EffectiveMode(a.ContentMode, newsSettings != null);
        // anything still waiting that belongs to the other mode (e.g. a script post on a news Page) is cancelled
        await CancelWrongModeAsync(pageId, mode, ct);
        await ReleaseCancelledScriptsAsync(pageId, ct);
        _allowUnassigned = mode == "Scripts" && !await OtherScriptPagesAsync(pageId, ct);

        var campaign = await EnsureCampaignAsync(pageId, a, Notice, ct);
        if (campaign != null && mode == "News")
        {
            if (newsSettings == null)
                Notice("error", "no_topic", "Page tin tức cần chủ đề: bật \"Tự động đăng\" cho Page (chọn chủ đề thể thao / tổng hợp…) hoặc chuyển chế độ sang Kịch bản.");
            else
            {
                var slots = await SlotsAsync(campaign, ct);
                await Step(state, "npost", force, Notice, () => PlanNewsPostAsync(campaign, pageId, newsSettings, today, slots, ct), ct);
                for (var n = 1; n <= a.StoriesPerDay; n++)
                {
                    var slot = $"nstory{n}";
                    var number = n;
                    await Step(state, slot, force, Notice, () => PlanNewsStoryAsync(campaign, pageId, newsSettings, today, slot, number, slots, ct), ct);
                }
            }
        }
        if (campaign != null && mode == "Scripts")
        {
            var slots = await SlotsAsync(campaign, ct);
            if (a.ScriptPost) await Step(state, "post", force, Notice, () => PlanScriptPostAsync(campaign, pageId, today, slots, Notice, ct), ct);
            if (a.DailyVideo) await Step(state, "reel", force, Notice, () => PlanReelAsync(campaign, pageId, today, slots, Notice, ct), ct);
            // the 2nd Story waits for today's post picture while that post can still be made today
            var postPending = a.ScriptPost && !state.Skipped.Contains("post") && state.Attempts.GetValueOrDefault("post") < MaxAttemptsPerDay;
            for (var n = 1; n <= a.StoriesPerDay; n++)
            {
                var slot = $"story{n}";
                var number = n;
                await Step(state, slot, force, Notice, () => PlanStoryAsync(campaign, pageId, today, slot, number, slots, postPending, ct), ct);
            }
        }

        var stats = await ScriptStatsAsync(pageId, ct);
        if (mode != "Scripts") { }
        else if (a.ScriptPost && stats.Total == 0) Notice("error", "no_scripts", "Chưa có kịch bản nào trong kho — mở trang Chiến dịch tăng trưởng để đồng bộ 300 kịch bản từ \"Lên kịch bản\".");
        else if (a.ScriptPost && stats.Remaining == 0) Notice("error", "scripts_empty", $"Đã dùng hết {stats.Total} kịch bản — hãy nhờ AI viết thêm kịch bản ở \"Lên kịch bản\" rồi đồng bộ. Bài viết hằng ngày đang dừng để không đăng trùng.");
        else if (a.ScriptPost && stats.Remaining <= LowScriptsWarning) Notice("warning", "scripts_low", $"Chỉ còn {stats.Remaining} kịch bản chưa dùng — nên chuẩn bị thêm.");

        a.LastRunAtUtc = now;
        a.NoticesJson = JsonSerializer.Serialize(notices, Json);
        a.StateJson = JsonSerializer.Serialize(state, Json);
        await _db.SaveChangesAsync(ct);
        return await GetAsync(pageId, ct);
    }

    private static DayState ReadState(GrowthAutopilot a, DateOnly today)
    {
        DayState? s = null;
        try { s = JsonSerializer.Deserialize<DayState>(a.StateJson, Json); } catch (JsonException) { }
        return s == null || s.Date != today.ToString("yyyy-MM-dd") ? new DayState { Date = today.ToString("yyyy-MM-dd") } : s;
    }

    /// <summary>One planning step with its own error handling and a daily attempt limit (a failing AI call is not repeated forever).</summary>
    private async Task Step(DayState state, string slot, bool force, Action<string, string, string> notice, Func<Task<string?>> run, CancellationToken ct)
    {
        if (!force && state.Skipped.Contains(slot)) return;
        var tries = state.Attempts.GetValueOrDefault(slot);
        if (!force && tries >= MaxAttemptsPerDay) { notice("warning", "gave_up:" + slot, $"{SlotLabel(slot)}: đã thử {tries} lần hôm nay không thành — để mai thử lại, hoặc bấm \"Chạy ngay\"."); return; }
        try
        {
            var skipped = await run();
            if (skipped != null)
            {
                // "!" marks a skip the owner should act on (e.g. no video to cut); the rest is information
                var warn = skipped.StartsWith('!');
                state.Skipped.Add(slot);
                notice(warn ? "warning" : "info", "skip:" + slot, $"{SlotLabel(slot)}: {skipped.TrimStart('!')}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            state.Attempts[slot] = tries + 1;
            if (_db is DbContext ctx) foreach (var e in ctx.ChangeTracker.Entries().Where(e => e.State == EntityState.Added && e.Entity is not GrowthActivityLog).ToList()) e.State = EntityState.Detached;
            var msg = GrowthGraphClient.Redact(ex.Message);
            notice("error", "failed:" + slot, $"{SlotLabel(slot)}: {msg}");
            _logger.LogWarning("Growth autopilot step {Slot} failed: {Message}", slot, msg);
        }
    }

    public static string SlotLabel(string slot) => slot switch
    {
        "post" => "Bài viết từ kịch bản",
        "reel" => "Video Reels hằng ngày",
        _ when slot.StartsWith("story") => "Tin " + slot[5..],
        _ => slot
    };

    /// <summary>The Page's running campaign, or a new 7-day "Tự động" campaign (auto posting) when there is none. Paused → null.</summary>
    private async Task<GrowthCampaign?> EnsureCampaignAsync(Guid pageId, GrowthAutopilot a, Action<string, string, string> notice, CancellationToken ct)
    {
        var current = await _db.GrowthCampaigns.FirstOrDefaultAsync(c => c.FacebookAccountId == pageId && (c.Status == GrowthStatus.Running || c.Status == GrowthStatus.Paused), ct);
        if (current?.Status == GrowthStatus.Paused)
        {
            notice("warning", "paused", $"Chiến dịch \"{current.Name}\" đang tạm dừng — chế độ tự động chờ tới khi tiếp tục.");
            return null;
        }
        if (current != null && GrowthTime.EndUtc(current.EndDate) > Clock()) return current;
        if (current != null) await _campaigns.CompleteAsync(current.Id, "scheduler", ct);
        var today = GrowthTime.Today(Clock());
        var created = await _campaigns.CreateAsync(new GrowthCampaignInputDto
        {
            Name = $"Tự động {today:dd/MM}–{today.AddDays(6):dd/MM}", FacebookAccountId = pageId, StartDate = today, Days = 7,
            ApprovalMode = "Auto", AiBudgetUsd = 2, ReelsPerDay = a.DailyVideo ? 1 : 0, PhotosPerDay = a.ScriptPost ? 1 : 0, StoriesPerDay = a.StoriesPerDay,
            TimeSlots = DefaultSlots.Values.ToList(),
            Notes = "Do chế độ tự động tạo. Báo cáo tuần được tạo khi chiến dịch kết thúc."
        }, ct);
        try
        {
            await _campaigns.StartAsync(created.Id, ct);
        }
        catch (InvalidOperationException ex)
        {
            notice("error", "start", "Không bắt đầu được chiến dịch tự động: " + ex.Message);
            return null;
        }
        notice("info", "campaign", $"Đã tạo và bắt đầu chiến dịch tuần mới \"{created.Name}\".");
        return await _db.GrowthCampaigns.FirstAsync(c => c.Id == created.Id, ct);
    }

    /// <summary>Times of the day: the defaults, the Reel moved to the Page's best hour once there is enough real data.</summary>
    private async Task<Dictionary<string, string>> SlotsAsync(GrowthCampaign c, CancellationToken ct)
    {
        var slots = new Dictionary<string, string>(DefaultSlots);
        try
        {
            var hours = await _insights.HoursAsync(c.Id, ct);
            if (hours.EnoughData && hours.SuggestedSlots.Count > 0)
            {
                var best = hours.SuggestedSlots.OrderByDescending(s => hours.Buckets.FirstOrDefault(b => $"{b.Hour:00}:00" == s)?.AvgViews ?? 0).First();
                slots["reel"] = best;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* keep the defaults */ }
        return slots;
    }

    /// <summary>When a slot goes out today: its time, or soon when that time has passed (null when it is too late today).</summary>
    public static DateTime? When(DateOnly today, string slot, int order, DateTime nowUtc, DateOnly campaignEnd)
    {
        var at = GrowthTime.SlotUtc(today, slot);
        var earliest = nowUtc.AddMinutes(15 + order * 6);
        if (at < earliest) at = earliest;
        var last = GrowthTime.EndUtc(today).AddMinutes(-30);
        if (at > last || at >= GrowthTime.EndUtc(campaignEnd)) return null;
        return DateTime.SpecifyKind(at, DateTimeKind.Utc);
    }

    private Task<bool> ExistsAsync(string key, CancellationToken ct) => _db.GrowthScheduledContents.AnyAsync(x => x.IdempotencyKey == key, ct);

    private async Task SaveNewAsync(GrowthScheduledContent item, CancellationToken ct)
    {
        _db.GrowthScheduledContents.Add(item);
        _db.GrowthActivityLogs.Add(new GrowthActivityLog
        {
            CampaignId = item.CampaignId, ContentId = item.Id, Action = "AutopilotPlanned", Actor = "scheduler",
            Message = $"Tự động lên lịch {GrowthContentService.KindLabel(item.Kind)} \"{GrowthContentService.Short(item.Title, item.Caption)}\" lúc {GrowthTime.ToLocal(item.ScheduledAtUtc!.Value):dd/MM HH:mm}."
        });
        await _db.SaveChangesAsync(ct);
    }


    // ═══════════════════════════════ modes ═══════════════════════════════

    /// <summary>Slots of the news mode start with "n" (npost, nstory1…); the script mode's don't (post, reel, story1…).</summary>
    public static bool IsNewsSlot(string idempotencyKey)
    {
        var parts = idempotencyKey.Split(':');
        return parts.Length >= 4 && parts[3].StartsWith('n');
    }

    /// <summary>Cancels waiting autopilot items of this Page made for the other content mode.</summary>
    private async Task CancelWrongModeAsync(Guid pageId, string mode, CancellationToken ct)
    {
        var prefix = $"auto:{pageId:N}:";
        var waiting = await _db.GrowthScheduledContents
            .Where(x => x.IdempotencyKey.StartsWith(prefix) && (x.Status == GrowthStatus.Scheduled || x.Status == GrowthStatus.PendingApproval || x.Status == GrowthStatus.Draft))
            .ToListAsync(ct);
        var wrong = waiting.Where(x => IsNewsSlot(x.IdempotencyKey) != (mode == "News")).ToList();
        foreach (var x in wrong)
        {
            x.Status = GrowthStatus.Cancelled;
            x.LastError = "Không đúng chủ đề của Page (chế độ nội dung đã đổi) — đã huỷ, không đăng.";
            _db.GrowthActivityLogs.Add(new GrowthActivityLog { CampaignId = x.CampaignId, ContentId = x.Id, Level = "Warning", Action = "AutopilotWrongMode", Message = x.LastError + $" \"{GrowthContentService.Short(x.Title, x.Caption)}\"", Actor = "scheduler" });
        }
        if (wrong.Count > 0) await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// For the publisher: an autopilot item that doesn't match its Page's content mode (a script post on a news Page, a
    /// news item on a nostalgia Page) must not go out.
    /// </summary>
    public static async Task<bool> WrongModeAsync(IApplicationDbContext db, GrowthScheduledContent item, Guid? pageId, CancellationToken ct)
    {
        if (pageId is not { } pid || !item.IdempotencyKey.StartsWith("auto:", StringComparison.Ordinal)) return false;
        var chosen = await db.GrowthAutopilots.AsNoTracking().Where(a => a.FacebookAccountId == pid).Select(a => a.ContentMode).FirstOrDefaultAsync(ct);
        var hasNews = await db.AutoPostSettings.AsNoTracking().AnyAsync(s => s.FacebookAccountId == pid && s.Enabled, ct);
        return IsNewsSlot(item.IdempotencyKey) != (GrowthNewsService.EffectiveMode(chosen, hasNews) == "News");
    }

    /// <summary>Another Page also runs the script (nostalgia) mode → videos without a Page are not used (they could be for it).</summary>
    private async Task<bool> OtherScriptPagesAsync(Guid pageId, CancellationToken ct)
    {
        var others = await _db.GrowthAutopilots.AsNoTracking().Where(a => a.Enabled && a.FacebookAccountId != pageId).Select(a => new { a.FacebookAccountId, a.ContentMode }).ToListAsync(ct);
        foreach (var o in others)
        {
            var hasNews = await _db.AutoPostSettings.AsNoTracking().AnyAsync(s => s.FacebookAccountId == o.FacebookAccountId && s.Enabled, ct);
            if (GrowthNewsService.EffectiveMode(o.ContentMode, hasNews) == "Scripts") return true;
        }
        return false;
    }

    // ═══════════════════════════════ news Pages ═══════════════════════════════

    /// <summary>
    /// The day's news photo post: the hottest unused story of the Page's topic, its own photo with a short headline +
    /// summary written on it, and a short post about it.
    /// </summary>
    private async Task<string?> PlanNewsPostAsync(GrowthCampaign c, Guid pageId, AutoPostSettings settings, DateOnly today, Dictionary<string, string> slots, CancellationToken ct)
    {
        var key = Key(pageId, today, "npost");
        if (await ExistsAsync(key, ct)) return null;
        var at = When(today, slots["post"], 0, Clock(), c.EndDate);
        if (at == null) return "đã quá giờ đăng hôm nay — bắt đầu từ ngày mai.";
        var topicName = NewsStudio.Infrastructure.Trending.NewsTopics.NameOf(settings.Topic);
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var article = await _news.PickAsync(pageId, settings, Clock(), ct, skip);
            if (article == null) return $"!không còn tin hot mới thuộc chủ đề \"{topicName}\" chưa đăng — để lần sau.";
            skip.Add(NewsStudio.Infrastructure.Services.AutoPostService.NormalizeUrl(article.Url));
            var text = await _news.ReadAsync(article, ct);
            var dir = StudioMedia.TempDir();
            try
            {
                var photo = await _news.DownloadPhotoAsync(text.ImageUrls, article.Url, Path.Combine(dir, "photo"), ct);
                if (photo == null) continue; // no usable picture in this article: next hottest
                var written = await _ai.NewsPostAsync(c, topicName, article, text.Text, ct);
                var page = await _db.FacebookAccounts.AsNoTracking().Where(x => x.Id == pageId).Select(x => x.PageName).FirstAsync(ct);
                var card = await _news.RenderCardAsync(pageId, page, settings.Topic, photo, written.Headline, written.Summary, article.SourceName, dir, ct);
                var asset = await _content.UploadAsync(c.Id, card, "tin-nong.jpg", "Template", ct);
                var item = new GrowthScheduledContent
                {
                    CampaignId = c.Id, Kind = GrowthKinds.Photo, Status = GrowthStatus.Scheduled, IdempotencyKey = key, ScheduledAtUtc = at,
                    Title = Trim(written.Headline, 300), Caption = written.Caption, Hashtags = written.Hashtags, Topic = Trim(topicName, 200), Style = "Knowledge",
                    AssetIdsJson = JsonSerializer.Serialize(new[] { asset.Id })
                };
                _db.GrowthNewsUsages.Add(new GrowthNewsUsage
                {
                    FacebookAccountId = pageId, Url = Trim(NewsStudio.Infrastructure.Services.AutoPostService.NormalizeUrl(article.Url), 800), TrendingArticleId = article.Id,
                    ClusterId = await _news.ClusterOfAsync(article.Id, ct), Title = Trim(article.Title, 500), Kind = "npost", ContentId = item.Id
                });
                try { await SaveNewAsync(item, ct); }
                catch (DbUpdateException) { if (_db is DbContext ctx) ctx.ChangeTracker.Clear(); }
                return null;
            }
            finally
            {
                StudioMedia.TryDelete(dir);
            }
        }
        throw new InvalidOperationException("Các tin hot nhất đều không có ảnh dùng được — sẽ thử lại sau.");
    }

    /// <summary>
    /// A voiced news Story: the hottest unused story of the topic is made into a short video by the news video pipeline
    /// (same voice and look as the auto-posted videos); when it is ready it is scheduled as a Story.
    /// </summary>
    private async Task<string?> PlanNewsStoryAsync(GrowthCampaign c, Guid pageId, AutoPostSettings settings, DateOnly today, string slot, int n,
        Dictionary<string, string> slots, CancellationToken ct)
    {
        var key = Key(pageId, today, slot);
        if (await ExistsAsync(key, ct)) return null;
        var dayStart = GrowthTime.DayStartUtc(today);
        var usage = await _db.GrowthNewsUsages.FirstOrDefaultAsync(u => u.FacebookAccountId == pageId && u.Kind == slot && u.CreatedAtUtc >= dayStart && u.ContentId == null, ct);
        var slotTime = slots.GetValueOrDefault(n == 1 ? "story1" : n == 2 ? "story2" : $"story{n}", "20:00");
        if (usage == null)
        {
            if (When(today, slotTime, 1 + n, Clock(), c.EndDate) == null) return "đã quá giờ đăng hôm nay — bắt đầu từ ngày mai.";
            var article = await _news.PickAsync(pageId, settings, Clock(), ct);
            if (article == null) return $"!không còn tin hot mới thuộc chủ đề \"{NewsStudio.Infrastructure.Trending.NewsTopics.NameOf(settings.Topic)}\" để làm Tin.";
            var projectId = await _news.StartStoryVideoAsync(pageId, settings, article, ct);
            _db.GrowthNewsUsages.Add(new GrowthNewsUsage
            {
                FacebookAccountId = pageId, Url = Trim(NewsStudio.Infrastructure.Services.AutoPostService.NormalizeUrl(article.Url), 800), TrendingArticleId = article.Id,
                ClusterId = await _news.ClusterOfAsync(article.Id, ct), Title = Trim(article.Title, 500), Kind = slot, VideoProjectId = projectId
            });
            await _db.SaveChangesAsync(ct);
            return null; // the video is being made: checked again on the next run
        }

        var project = await _db.VideoProjects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == usage.VideoProjectId, ct);
        var failed = project == null || project.Status is NewsStudio.Domain.Enums.VideoProjectStatus.Failed or NewsStudio.Domain.Enums.VideoProjectStatus.Cancelled
                     || Clock() - usage.CreatedAtUtc > TimeSpan.FromMinutes(90) && project.Status != NewsStudio.Domain.Enums.VideoProjectStatus.AwaitingApproval;
        if (failed)
        {
            usage.Kind = "failed"; // frees the slot; the story stays marked as used so it isn't tried again
            await _db.SaveChangesAsync(ct);
            throw new InvalidOperationException("Dựng video tin cho Tin không thành: " + (project?.ErrorMessage ?? "quá lâu") + " — sẽ chọn tin khác.");
        }
        if (project!.Status != NewsStudio.Domain.Enums.VideoProjectStatus.AwaitingApproval) return null; // still being made

        var at = When(today, slotTime, 1 + n, Clock(), c.EndDate);
        if (at == null) return "video xong muộn — quá giờ đăng hôm nay.";
        var item = new GrowthScheduledContent
        {
            CampaignId = c.Id, Kind = GrowthKinds.Story, Status = GrowthStatus.Scheduled, IdempotencyKey = key, ScheduledAtUtc = at,
            Title = "Tin · " + Trim(project.Title.Length > 0 ? project.Title : usage.Title, 280), SourceType = GrowthSources.VideoProject, SourceId = project.Id
        };
        usage.ContentId = item.Id;
        try { await SaveNewAsync(item, ct); }
        catch (DbUpdateException) { if (_db is DbContext ctx) ctx.ChangeTracker.Clear(); }
        return null;
    }

    // ── the script post ──

    private async Task<string?> PlanScriptPostAsync(GrowthCampaign c, Guid pageId, DateOnly today, Dictionary<string, string> slots, Action<string, string, string> notice, CancellationToken ct)
    {
        var key = Key(pageId, today, "post");
        if (await ExistsAsync(key, ct)) return null;
        var at = When(today, slots["post"], 0, Clock(), c.EndDate);
        if (at == null) return "đã quá giờ đăng hôm nay — bắt đầu từ ngày mai.";
        var script = await NextScriptAsync(pageId, ct);
        if (script == null) return null; // the notice about scripts is added by RunAsync

        // 1. the picture (AI) with the Page watermark
        var (bytes, mime) = await _ai.ScriptImageAsync(c, script, ct);
        var dir = StudioMedia.TempDir();
        GrowthAssetDto asset;
        try
        {
            var raw = Path.Combine(dir, mime.Contains("png") ? "raw.png" : "raw.jpg");
            await File.WriteAllBytesAsync(raw, bytes, ct);
            var final = Path.Combine(dir, "post.jpg");
            try
            {
                var (brand, logo) = await _studio.BrandAsync(pageId, ct);
                var wm = Path.Combine(dir, "watermark.png");
                await _studioMedia.CreateWatermarkAsync(brand, logo, wm, ct);
                await _media.WatermarkImageAsync(raw, wm, final, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                notice("warning", "watermark", "Không gắn được watermark Page lên ảnh (đăng ảnh không watermark): " + ex.Message);
                await _media.ToJpegAsync(raw, final, ct);
            }
            asset = await _content.UploadAsync(c.Id, final, $"kich-ban-{script.Code}.jpg", "Ai", ct);
        }
        finally
        {
            StudioMedia.TryDelete(dir);
        }

        // 2. the text
        var (caption, hashtags) = await _ai.ScriptPostAsync(c, script, ct);

        // 3. the post + the script marked as used on this Page (unique: never twice)
        var item = new GrowthScheduledContent
        {
            CampaignId = c.Id, Kind = GrowthKinds.Photo, Status = GrowthStatus.Scheduled, IdempotencyKey = key, ScheduledAtUtc = at,
            Title = Trim(script.Title, 300), Caption = caption, Hashtags = hashtags, Style = "Nostalgic", Topic = Trim(script.CategoryName ?? "", 200),
            AssetIdsJson = JsonSerializer.Serialize(new[] { asset.Id }), ScriptId = script.Id
        };
        _db.GrowthScriptUsages.Add(new GrowthScriptUsage { FacebookAccountId = pageId, ScriptId = script.Id, ContentId = item.Id });
        try
        {
            await SaveNewAsync(item, ct);
        }
        catch (DbUpdateException)
        {
            if (_db is DbContext ctx) ctx.ChangeTracker.Clear();
            return "đã có bài hôm nay (một máy khác vừa tạo).";
        }
        return null;
    }

    /// <summary>A script whose post was cancelled before it went out becomes available again.</summary>
    private async Task ReleaseCancelledScriptsAsync(Guid pageId, CancellationToken ct)
    {
        var cancelled = await _db.GrowthScriptUsages
            .Where(u => u.FacebookAccountId == pageId && u.ContentId != null
                        && _db.GrowthScheduledContents.Any(x => x.Id == u.ContentId && x.Status == GrowthStatus.Cancelled && x.PublishedAtUtc == null))
            .ToListAsync(ct);
        if (cancelled.Count == 0) return;
        _db.GrowthScriptUsages.RemoveRange(cancelled);
        await _db.SaveChangesAsync(ct);
    }

    // ── the daily Reel ──

    private async Task<string?> PlanReelAsync(GrowthCampaign c, Guid pageId, DateOnly today, Dictionary<string, string> slots, Action<string, string, string> notice, CancellationToken ct)
    {
        var key = Key(pageId, today, "reel");
        if (await ExistsAsync(key, ct)) return null;
        var at = When(today, slots["reel"], 1, Clock(), c.EndDate);
        if (at == null) return "đã quá giờ đăng hôm nay — bắt đầu từ ngày mai.";
        var scheduledReels = _db.GrowthScheduledContents.Where(x => x.Kind == GrowthKinds.Reel && x.Status != GrowthStatus.Cancelled && x.SourceId != null).Select(x => x.SourceId!.Value);

        string? type = null;
        Guid id = default;
        string title = "", caption = "";
        var studio = await _db.StudioVideos.AsNoTracking()
            .Where(v => v.Status == "Ready" && v.FinalBlobId != null && v.PublishedPostId == null && (v.FacebookAccountId == pageId || (_allowUnassigned && v.FacebookAccountId == null))
                        && !scheduledReels.Contains(v.Id))
            .OrderByDescending(v => v.FacebookAccountId == pageId).ThenByDescending(v => v.UpdatedAtUtc ?? v.CreatedAtUtc)
            .Select(v => new { v.Id, v.Title, v.Caption }).FirstOrDefaultAsync(ct);
        if (studio != null) { type = GrowthSources.StudioVideo; id = studio.Id; title = studio.Title; caption = studio.Caption ?? ""; }
        else
        {
            var ai = await _db.AiStudioProjects.AsNoTracking()
                .Where(p => p.FinalStatus == "Done" && p.FinalBlobId != null && (p.FacebookAccountId == pageId || (_allowUnassigned && p.FacebookAccountId == null)) && !scheduledReels.Contains(p.Id))
                .OrderByDescending(p => p.FacebookAccountId == pageId).ThenByDescending(p => p.FinalUpdatedAtUtc ?? p.CreatedAtUtc)
                .Select(p => new { p.Id, p.Title, p.Caption }).FirstOrDefaultAsync(ct);
            if (ai != null) { type = GrowthSources.AiStudioProject; id = ai.Id; title = ai.Title; caption = ai.Caption; }
        }
        if (type == null)
        {
            notice("warning", "no_video", "Hôm nay chưa có video mới hoàn thành để đăng Reels — tạo 1 video ở Studio Video AI hoặc AI Video Studio (ứng dụng sẽ tự đăng khi có).");
            return null; // checked again on the next run today
        }
        var hashtags = "";
        if (string.IsNullOrWhiteSpace(caption))
        {
            try { (caption, hashtags) = await _ai.ReelCaptionAsync(c, type, id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { caption = title; notice("warning", "reel_caption", "AI chưa viết được caption Reels — dùng tên video: " + ex.Message); }
        }
        var item = new GrowthScheduledContent
        {
            CampaignId = c.Id, Kind = GrowthKinds.Reel, Status = GrowthStatus.Scheduled, IdempotencyKey = key, ScheduledAtUtc = at,
            Title = Trim(title, 300), Caption = Trim(caption, GrowthContentService.MaxCaption), Hashtags = hashtags, SourceType = type, SourceId = id, Style = "Nostalgic"
        };
        try { await SaveNewAsync(item, ct); }
        catch (DbUpdateException) { if (_db is DbContext ctx) ctx.ChangeTracker.Clear(); }
        return null;
    }

    // ── Stories ──

    private async Task<string?> PlanStoryAsync(GrowthCampaign c, Guid pageId, DateOnly today, string slot, int n, Dictionary<string, string> slots, bool postPending, CancellationToken ct)
    {
        var key = Key(pageId, today, slot);
        if (await ExistsAsync(key, ct)) return null;
        var at = When(today, slots.GetValueOrDefault(slot, "20:00"), 1 + n, Clock(), c.EndDate);
        if (at == null) return "đã quá giờ đăng hôm nay — bắt đầu từ ngày mai.";
        var item = new GrowthScheduledContent { CampaignId = c.Id, Kind = GrowthKinds.Story, Status = GrowthStatus.Scheduled, IdempotencyKey = key, ScheduledAtUtc = at };

        // the 2nd Story is the day's script picture (once the post is ready)
        if (n == 2)
        {
            var post = await _db.GrowthScheduledContents.AsNoTracking().FirstOrDefaultAsync(x => x.IdempotencyKey == Key(pageId, today, "post"), ct);
            var assetId = post == null ? (Guid?)null : GrowthContentService.AssetIds(post.AssetIdsJson).FirstOrDefault();
            if (assetId is { } aid && aid != Guid.Empty)
            {
                item.Title = "Tin · " + Trim(post!.Title, 280);
                item.AssetIdsJson = JsonSerializer.Serialize(new[] { aid });
                try { await SaveNewAsync(item, ct); }
                catch (DbUpdateException) { if (_db is DbContext ctx) ctx.ChangeTracker.Clear(); }
                return null;
            }
            if (postPending) return null; // try again once the post is ready
        }

        // otherwise: the liveliest part of a Page video not used for a Story yet
        var highlight = await FindHighlightAsync(pageId, ct);
        if (highlight == null) return "!chưa có video nào còn đoạn chưa dùng để cắt Tin — hôm nay bỏ qua (bấm \"Chạy ngay\" sau khi có video mới).";
        item.Title = "Tin · " + Trim(highlight.Value.Title, 280);
        item.SourceType = highlight.Value.Type;
        item.SourceId = highlight.Value.Id;
        item.ClipStartSeconds = Math.Round(highlight.Value.Start, 1);
        item.ClipSeconds = Math.Round(highlight.Value.Seconds, 1);
        try { await SaveNewAsync(item, ct); }
        catch (DbUpdateException) { if (_db is DbContext ctx) ctx.ChangeTracker.Clear(); }
        return null;
    }

    private async Task<(string Type, Guid Id, string Title, double Start, double Seconds)?> FindHighlightAsync(Guid pageId, CancellationToken ct)
    {
        var studio = await _db.StudioVideos.AsNoTracking()
            .Where(v => v.FinalBlobId != null && (v.FacebookAccountId == pageId || (_allowUnassigned && v.FacebookAccountId == null)))
            .OrderByDescending(v => v.PublishedAtUtc != null).ThenByDescending(v => v.PublishedAtUtc ?? v.CreatedAtUtc)
            .Select(v => new { v.Id, v.Title, Blob = v.FinalBlobId!.Value }).Take(8).ToListAsync(ct);
        var ai = await _db.AiStudioProjects.AsNoTracking()
            .Where(p => p.FinalStatus == "Done" && p.FinalBlobId != null && (p.FacebookAccountId == pageId || (_allowUnassigned && p.FacebookAccountId == null)))
            .OrderByDescending(p => p.FinalUpdatedAtUtc ?? p.CreatedAtUtc).Select(p => new { p.Id, p.Title, Blob = p.FinalBlobId!.Value }).Take(8).ToListAsync(ct);
        var candidates = studio.Select(v => (Type: GrowthSources.StudioVideo, v.Id, v.Title, v.Blob))
            .Concat(ai.Select(p => (Type: GrowthSources.AiStudioProject, p.Id, p.Title, p.Blob))).ToList();
        foreach (var cand in candidates)
        {
            var used = await _db.GrowthScheduledContents.AsNoTracking()
                .Where(x => x.Kind == GrowthKinds.Story && x.SourceId == cand.Id && x.Status != GrowthStatus.Cancelled)
                .Select(x => new { x.ClipStartSeconds, x.ClipSeconds }).ToListAsync(ct);
            var windows = used.Select(u => (u.ClipStartSeconds ?? 0, u.ClipSeconds ?? 9999)).ToList();
            string? path;
            try
            {
                path = cand.Type == GrowthSources.StudioVideo ? (await _studio.DiskFileAsync(cand.Blob, ct))?.Path : (await _aiStudio.FileAsync(cand.Blob, ct))?.Path;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { path = null; }
            if (path == null) continue;
            double duration;
            try { duration = (await _probe.ProbeAsync(path, ct)).Duration; }
            catch (InvalidOperationException) { continue; }
            var loudness = duration > GrowthMedia.MaxStorySeconds ? await _media.LoudnessAsync(path, ct) : new();
            if (GrowthMedia.PickWindow(loudness, duration, GrowthMedia.StorySeconds, windows) is { } w)
                return (cand.Type, cand.Id, cand.Title, w.Start, w.Seconds);
        }
        return null;
    }

    private static string Trim(string s, int max) => s.Length > max ? s[..max] : s;
}
