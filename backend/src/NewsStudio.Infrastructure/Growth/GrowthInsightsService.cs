using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Infrastructure.Growth;

/// <summary>
/// Numbers of a campaign, only as Meta reports them: followers, Page day metrics (each tried on its own — Meta retired
/// several), per-post numbers. A number Meta doesn't give stays null and is shown as "Chưa có dữ liệu" with the reason;
/// nothing is estimated or filled in.
/// </summary>
public class GrowthInsightsService
{
    public const int HourMinSamples = 8;
    public const int BeforeDays = 7;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly GrowthGraphClient _graph;
    private readonly IFacebookPublishingService _facebook;
    private readonly GrowthCampaignService _campaigns;
    private readonly GrowthContentService _content;
    private readonly ILogger<GrowthInsightsService> _logger;

    public GrowthInsightsService(IApplicationDbContext db, GrowthGraphClient graph, IFacebookPublishingService facebook, GrowthCampaignService campaigns,
        GrowthContentService content, ILogger<GrowthInsightsService> logger)
    {
        _db = db;
        _graph = graph;
        _facebook = facebook;
        _campaigns = campaigns;
        _content = content;
        _logger = logger;
    }

    // ═══════════════════════════════ sync from Meta ═══════════════════════════════

    /// <summary>Recurring (hourly): running / paused campaigns, and completed ones for 3 days after their end (late numbers).</summary>
    [Hangfire.AutomaticRetry(Attempts = 0)]
    [Hangfire.DisableConcurrentExecution(600)]
    public async Task SyncAllAsync(CancellationToken ct)
    {
        var today = GrowthTime.Today(DateTime.UtcNow);
        var ids = await _db.GrowthCampaigns.AsNoTracking()
            .Where(c => c.FacebookAccountId != null && (c.Status == GrowthStatus.Running || c.Status == GrowthStatus.Paused
                        || (c.Status == GrowthStatus.Completed && c.EndDate >= today.AddDays(-3))))
            .Select(c => c.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            try { await SyncAsync(id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Growth sync of campaign {Id} failed: {Message}", id, ex.Message);
            }
        }
    }

    public async Task<GrowthSyncResultDto> SyncAsync(Guid campaignId, CancellationToken ct)
    {
        var c = await _campaigns.LoadAsync(campaignId, ct);
        var result = new GrowthSyncResultDto();
        if (c.FacebookAccountId is not { } pageId)
        {
            result.Errors.Add("Page của chiến dịch đã bị xoá khỏi ứng dụng.");
            return result;
        }
        var now = DateTime.UtcNow;
        var today = GrowthTime.Today(now);

        // 1. followers now
        var (followers, followersError) = await _graph.FollowersAsync(pageId, ct);
        if (followers != null)
        {
            var row = await DayRowAsync(pageId, today, ct);
            row.Followers = followers;
            row.CollectedAtUtc = now;
            result.FollowersRead = true;
        }
        else if (followersError != null) result.Errors.Add(GrowthGraphClient.Redact(followersError));

        // 2. page day metrics, from a week before the start (for the before / during comparison)
        var from = c.StartDate.AddDays(-BeforeDays);
        var to = c.EndDate < today ? c.EndDate : today;
        var availability = new Dictionary<string, GrowthMetricAvailabilityDto>();
        if (to >= from)
        {
            var series = await _graph.PageInsightsAsync(pageId, from, to, GrowthGraphClient.PageDayMetrics, ct);
            foreach (var s in series)
            {
                availability[s.Metric] = new GrowthMetricAvailabilityDto { Metric = s.Metric, Ok = s.Error == null, Error = s.Error, AtUtc = now };
                if (s.Error != null) { result.MetricsUnavailable++; continue; }
                result.MetricsAvailable++;
                foreach (var (day, value) in s.Values)
                {
                    var row = await DayRowAsync(pageId, day, ct);
                    switch (s.Metric)
                    {
                        case "page_daily_follows_unique": row.NewFollows = value; break;
                        case "page_daily_unfollows_unique": row.Unfollows = value; break;
                        case "page_views_total": row.PageViews = value; break;
                        case "page_media_view": row.MediaViews = value; break;
                        case "page_total_media_view_unique": row.MediaViewers = value; break;
                        case "page_post_engagements": row.Engagements = value; break;
                    }
                    row.CollectedAtUtc = now;
                }
            }
        }

        // 3. each published item (last 30 days)
        var items = await _db.GrowthScheduledContents
            .Where(x => x.CampaignId == c.Id && x.Status == GrowthStatus.Published && x.ExternalPostId != null && !x.PublishedManually
                        && x.PublishedAtUtc >= now.AddDays(-30) && x.Kind != GrowthKinds.Story)
            .ToListAsync(ct);
        var reelFollowsError = (string?)null;
        foreach (var item in items)
        {
            var metric = await PostRowAsync(item.Id, today, ct);
            metric.CollectedAtUtc = now;
            try
            {
                if (item.Kind == GrowthKinds.Reel && item.FacebookPostRecordId is { } recordId)
                {
                    var m = await _facebook.FetchMetricsAsync(recordId, ct); // existing Reels reader (also keeps the app's own stats up to date)
                    if (m != null)
                    {
                        metric.Views = m.Views;
                        metric.Reach = m.Reach;
                        metric.Reactions = m.Engagement;
                        metric.Comments = m.Comments;
                        metric.Shares = m.Shares;
                    }
                    if (item.ExternalVideoId != null)
                    {
                        var (follows, error) = await _graph.ReelFollowsAsync(pageId, item.ExternalVideoId, ct);
                        metric.Follows = follows;
                        reelFollowsError ??= error;
                    }
                    metric.Error = null;
                }
                else if (item.Kind != GrowthKinds.Reel)
                {
                    var s = await _graph.PostStatsAsync(pageId, item.ExternalPostId!, ct);
                    metric.Views = s.Views;
                    metric.Reach = s.Reach;
                    metric.Reactions = s.Reactions;
                    metric.Comments = s.Comments;
                    metric.Shares = s.Shares;
                    metric.Clicks = s.Clicks;
                    metric.Error = s.Errors.Count > 0 ? Cut(string.Join(" | ", s.Errors.Distinct()), 1000) : null;
                }
                result.PostsRead++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                metric.Error = Cut(ex.Message, 1000);
                result.PostErrors++;
            }
        }
        if (items.Any(i => i.Kind == GrowthKinds.Reel))
            availability["post_video_followers"] = new GrowthMetricAvailabilityDto { Metric = "post_video_followers", Ok = reelFollowsError == null, Error = reelFollowsError, AtUtc = now };

        c.MetricAvailabilityJson = JsonSerializer.Serialize(availability.ToDictionary(k => k.Key, v => v.Value), Json);
        c.LastSyncedAtUtc = now;
        c.LastSyncError = result.Errors.Count > 0 ? Cut(string.Join(" | ", result.Errors), 2000) : null;
        await _db.SaveChangesAsync(ct);
        return result;
    }

    private async Task<GrowthDailyMetric> DayRowAsync(Guid pageId, DateOnly day, CancellationToken ct)
    {
        var local = (_db as DbContext)?.ChangeTracker.Entries<GrowthDailyMetric>().Select(e => e.Entity)
            .FirstOrDefault(r => r.FacebookAccountId == pageId && r.Date == day);
        if (local != null) return local;
        var row = await _db.GrowthDailyMetrics.FirstOrDefaultAsync(r => r.FacebookAccountId == pageId && r.Date == day, ct);
        if (row != null) return row;
        row = new GrowthDailyMetric { FacebookAccountId = pageId, Date = day };
        _db.GrowthDailyMetrics.Add(row);
        return row;
    }

    private async Task<GrowthPostMetric> PostRowAsync(Guid contentId, DateOnly day, CancellationToken ct)
    {
        var row = await _db.GrowthPostMetrics.FirstOrDefaultAsync(r => r.ContentId == contentId && r.Date == day, ct);
        if (row != null) return row;
        row = new GrowthPostMetric { ContentId = contentId, Date = day };
        _db.GrowthPostMetrics.Add(row);
        return row;
    }

    // ═══════════════════════════════ data loading ═══════════════════════════════

    private sealed record Loaded(
        GrowthCampaign Campaign, GrowthCampaignDto Dto, List<GrowthScheduledContent> Items, Dictionary<Guid, List<GrowthPostMetric>> Metrics,
        List<GrowthDailyMetric> Days, Dictionary<DateOnly, long> Snapshots);

    private async Task<Loaded> LoadAsync(Guid campaignId, CancellationToken ct)
    {
        var c = await _db.GrowthCampaigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == campaignId, ct) ?? throw new KeyNotFoundException("Không tìm thấy chiến dịch.");
        var dto = await _campaigns.DtoAsync(c, ct);
        var items = await _db.GrowthScheduledContents.AsNoTracking().Where(x => x.CampaignId == c.Id).ToListAsync(ct);
        var ids = items.Select(i => i.Id).ToList();
        var metrics = (await _db.GrowthPostMetrics.AsNoTracking().Where(m => ids.Contains(m.ContentId)).ToListAsync(ct))
            .GroupBy(m => m.ContentId).ToDictionary(g => g.Key, g => g.OrderBy(m => m.Date).ToList());
        var from = c.StartDate.AddDays(-BeforeDays);
        var days = new List<GrowthDailyMetric>();
        var snaps = new Dictionary<DateOnly, long>();
        if (c.FacebookAccountId is { } pid)
        {
            days = await _db.GrowthDailyMetrics.AsNoTracking().Where(d => d.FacebookAccountId == pid && d.Date >= from && d.Date <= c.EndDate).ToListAsync(ct);
            snaps = await _db.PageFollowerSnapshots.AsNoTracking().Where(s => s.FacebookAccountId == pid && s.Date >= from && s.Date <= c.EndDate)
                .ToDictionaryAsync(s => s.Date, s => s.FacebookFollowers, ct);
        }
        return new Loaded(c, dto, items, metrics, days, snaps);
    }

    private static GrowthPostMetric? Latest(Loaded l, Guid id) => l.Metrics.TryGetValue(id, out var list) && list.Count > 0 ? list[^1] : null;

    private static long? Followers(Loaded l, DateOnly day) =>
        l.Days.FirstOrDefault(d => d.Date == day)?.Followers ?? (l.Snapshots.TryGetValue(day, out var v) ? v : null);

    public static long? SumOrNull(IEnumerable<long?> values)
    {
        long sum = 0;
        var any = false;
        foreach (var v in values)
        {
            if (v == null) continue;
            sum += v.Value;
            any = true;
        }
        return any ? sum : null;
    }

    /// <summary>New followers ÷ starting followers × 100 (null when either is missing or starting is 0).</summary>
    public static double? GrowthPercent(long? newFollowers, long? starting) =>
        newFollowers is { } n && starting is { } s && s > 0 ? Math.Round(n * 100.0 / s, 2) : null;

    /// <summary>New followers ÷ page visits × 100 (null when either is missing or visits is 0).</summary>
    public static double? ConversionPercent(long? newFollowers, long? visits) =>
        newFollowers is { } n && visits is { } v && v > 0 ? Math.Round(n * 100.0 / v, 2) : null;

    /// <summary>Views (or another lifetime counter) gained per day from daily lifetime snapshots of each post.</summary>
    public static Dictionary<DateOnly, long> DailyGains(IEnumerable<List<GrowthPostMetric>> perPost, Func<GrowthPostMetric, long?> pick)
    {
        var gains = new Dictionary<DateOnly, long>();
        foreach (var list in perPost)
        {
            long prev = 0;
            foreach (var m in list.OrderBy(m => m.Date))
            {
                if (pick(m) is not { } v) continue;
                var gain = Math.Max(0, v - prev);
                gains[m.Date] = gains.GetValueOrDefault(m.Date) + gain;
                prev = Math.Max(prev, v);
            }
        }
        return gains;
    }

    private static GrowthPeriodDto Period(Loaded l, DateOnly from, DateOnly to)
    {
        var rows = l.Days.Where(d => d.Date >= from && d.Date <= to).ToList();
        long? start = null, end = null;
        for (var d = from; d <= to; d = d.AddDays(1)) if (Followers(l, d) is { } f) { start = f; break; }
        for (var d = to; d >= from; d = d.AddDays(-1)) if (Followers(l, d) is { } f) { end = f; break; }
        return new GrowthPeriodDto
        {
            From = from, To = to, FollowersStart = start, FollowersEnd = end,
            FollowerGain = start != null && end != null ? end - start : null,
            NewFollows = SumOrNull(rows.Select(r => r.NewFollows)), PageViews = SumOrNull(rows.Select(r => r.PageViews)),
            MediaViews = SumOrNull(rows.Select(r => r.MediaViews)), Engagements = SumOrNull(rows.Select(r => r.Engagements)),
            DaysWithData = rows.Count(r => r.NewFollows != null || r.PageViews != null || r.MediaViews != null || r.Followers != null)
        };
    }

    private static string? AvailabilityError(Loaded l, string metric) =>
        l.Dto.MetricAvailability.FirstOrDefault(a => a.Metric == metric) is { Ok: false } a ? a.Error : null;

    private static string MissingNote(Loaded l, string metric, string fallback) =>
        AvailabilityError(l, metric) is { } e ? $"Meta không trả về {metric} cho Page này: {e}" :
        l.Dto.LastSyncedAtUtc == null ? "Chưa đọc số liệu từ Meta — bấm \"Đồng bộ số liệu\"." : fallback;

    // ═══════════════════════════════ overview ═══════════════════════════════

    public async Task<GrowthOverviewDto> OverviewAsync(Guid campaignId, CancellationToken ct)
    {
        var l = await LoadAsync(campaignId, ct);
        var c = l.Campaign;
        var now = DateTime.UtcNow;
        var today = GrowthTime.Today(now);
        var to = c.EndDate < today ? c.EndDate : today;
        var dto = new GrowthOverviewDto { Campaign = l.Dto };
        if (c.FacebookAccountId is { } pid)
            dto.Page = (await _campaigns.PagesAsync(ct)).FirstOrDefault(p => p.Id == pid);

        // followers now: today's reading, else the latest one
        var latestDay = l.Days.Where(d => d.Followers != null).OrderByDescending(d => d.Date).FirstOrDefault();
        var latestSnap = l.Snapshots.OrderByDescending(kv => kv.Key).Select(kv => (DateOnly?)kv.Key).FirstOrDefault();
        if (latestDay != null && (latestSnap == null || latestDay.Date >= latestSnap)) { dto.CurrentFollowers = latestDay.Followers; dto.CurrentFollowersAtUtc = latestDay.CollectedAtUtc; }
        else if (latestSnap is { } sd) { dto.CurrentFollowers = l.Snapshots[sd]; dto.CurrentFollowersAtUtc = GrowthTime.DayStartUtc(sd); }
        if (c.Status is GrowthStatus.Completed or GrowthStatus.Cancelled && c.EndingFollowers != null)
        {
            dto.CurrentFollowers = c.EndingFollowers;
            dto.CurrentFollowersAtUtc = c.EndingFollowersAtUtc;
        }

        var published = l.Items.Where(i => i.Status == GrowthStatus.Published).ToList();
        var latest = published.Select(i => (Item: i, M: Latest(l, i.Id))).ToList();
        var during = Period(l, c.StartDate, to < c.StartDate ? c.StartDate : to);
        var manualVisits = SumOrNull(l.Days.Where(d => d.Date >= c.StartDate && d.Date <= to).Select(d => d.ManualPageVisits));

        long? newFollowers = c.StartingFollowers is { } sf && dto.CurrentFollowers is { } cf && c.Status != GrowthStatus.Draft ? cf - sf : during.NewFollows;
        var visits = during.PageViews ?? manualVisits;
        var views = SumOrNull(latest.Select(x => x.M?.Views));
        var reach = SumOrNull(latest.Select(x => x.M?.Reach));
        var reactions = SumOrNull(latest.Select(x => x.M?.Reactions));
        var comments = SumOrNull(latest.Select(x => x.M?.Comments));
        var shares = SumOrNull(latest.Select(x => x.M?.Shares));
        var engagement = SumOrNull(latest.Select(x => x.M == null ? null : GrowthContentService.Engagement(x.M)));
        var noPostNumbers = published.Count == 0 ? "Chưa có bài nào được đăng trong chiến dịch." : "Chưa đọc được số liệu bài đăng — bấm \"Đồng bộ số liệu\".";

        dto.Kpis = new List<GrowthKpiDto>
        {
            new() { Key = "new_followers", Label = "Follower mới", Value = newFollowers,
                Source = c.StartingFollowers != null ? "meta" : "meta",
                Note = newFollowers == null ? (c.Status == GrowthStatus.Draft ? "Chiến dịch chưa bắt đầu." : c.StartingFollowers == null ? "Chưa đọc được follower đầu kỳ từ Meta." : "Chưa đọc được follower hiện tại.") : null },
            new() { Key = "views", Label = "Tổng lượt xem", Value = views, Source = "meta", Note = views == null ? noPostNumbers : "Cộng lượt xem các bài của chiến dịch (Reels: lượt phát; ảnh: lượt xem media)." },
            new() { Key = "reach", Label = "Lượt tiếp cận", Value = reach, Source = "meta", Note = reach == null ? noPostNumbers : "Cộng reach từng bài — một người có thể được đếm ở nhiều bài." },
            new() { Key = "non_follower_views", Label = "Lượt xem từ người chưa theo dõi", Value = null, Source = "meta",
                Note = "Meta không cung cấp phân tách người theo dõi / chưa theo dõi qua Graph API cho Page — xem trong Meta Business Suite → Thông tin chi tiết." },
            new() { Key = "engagement", Label = "Tổng tương tác", Value = engagement, Source = "meta", Note = engagement == null ? noPostNumbers : "Cảm xúc + bình luận + chia sẻ." },
            new() { Key = "shares", Label = "Lượt chia sẻ", Value = shares, Source = "meta", Note = shares == null ? noPostNumbers : null },
            new() { Key = "comments", Label = "Lượt bình luận", Value = comments, Source = "meta", Note = comments == null ? noPostNumbers : null },
            new() { Key = "page_visits", Label = "Lượt truy cập Page", Value = visits, Source = during.PageViews != null ? "meta" : "manual",
                Note = visits == null ? MissingNote(l, "page_views_total", "Meta chưa có số liệu ngày nào trong chiến dịch.") + " Có thể nhập tay ở Phân tích tăng trưởng." : during.PageViews == null ? "Số nhập tay." : null },
            new() { Key = "posts", Label = "Số bài đã đăng", Value = published.Count, Source = "app" },
            new() { Key = "reels", Label = "Reels đã đăng", Value = published.Count(i => i.Kind == GrowthKinds.Reel), Source = "app" },
            new() { Key = "stories", Label = "Story đã đăng", Value = published.Count(i => i.Kind == GrowthKinds.Story), Source = "app" },
            new() { Key = "growth_rate", Label = "Tỷ lệ tăng follower", Unit = "%", Value = GrowthPercent(newFollowers, c.StartingFollowers), Source = "computed",
                Note = GrowthPercent(newFollowers, c.StartingFollowers) == null ? "Cần follower đầu kỳ và follower mới." : "Follower mới ÷ follower đầu kỳ × 100%." },
            new() { Key = "conversion", Label = "Chuyển đổi truy cập → follower", Unit = "%", Value = ConversionPercent(newFollowers, visits), Source = "computed",
                Note = ConversionPercent(newFollowers, visits) == null ? "Cần follower mới và lượt truy cập Page." : "Follower mới ÷ lượt truy cập Page × 100%." },
        };

        // day series: campaign days plus the week before
        var gainsViews = DailyGains(l.Metrics.Values, m => m.Views);
        var gainsEng = DailyGains(l.Metrics.Values, GrowthContentService.Engagement);
        for (var d = c.StartDate.AddDays(-BeforeDays); d <= to; d = d.AddDays(1))
        {
            var row = l.Days.FirstOrDefault(x => x.Date == d);
            dto.Daily.Add(new GrowthDayPointDto
            {
                Date = d, Followers = Followers(l, d), NewFollows = row?.NewFollows, Unfollows = row?.Unfollows, PageViews = row?.PageViews,
                MediaViews = row?.MediaViews, MediaViewers = row?.MediaViewers, Engagements = row?.Engagements,
                ContentViews = gainsViews.TryGetValue(d, out var gv) ? gv : null, ContentEngagement = gainsEng.TryGetValue(d, out var ge) ? ge : null,
                Published = published.Count(i => i.PublishedAtUtc is { } p && GrowthTime.DateOf(p) == d),
                ManualInvitesSent = row?.ManualInvitesSent, ManualInvitesAccepted = row?.ManualInvitesAccepted,
                ManualPageVisits = row?.ManualPageVisits, ManualNewFollowers = row?.ManualNewFollowers
            });
        }

        dto.ByKind = ByKind(published, l);
        dto.Before = Period(l, c.StartDate.AddDays(-BeforeDays), c.StartDate.AddDays(-1));
        dto.During = during;

        var items = await _content.ToDtosAsync(l.Items.Where(i => i.Status is not GrowthStatus.Cancelled).ToList(), ct);
        var todayStart = GrowthTime.DayStartUtc(today);
        var todayEnd = todayStart.AddDays(1);
        dto.Today = items.Where(i => (i.ScheduledAtUtc >= todayStart && i.ScheduledAtUtc < todayEnd) || (i.PublishedAtUtc >= todayStart && i.PublishedAtUtc < todayEnd))
            .OrderBy(i => i.ScheduledAtUtc).ToList();
        dto.Upcoming = items.Where(i => i.Status is GrowthStatus.Scheduled or GrowthStatus.PendingApproval && i.ScheduledAtUtc >= now)
            .OrderBy(i => i.ScheduledAtUtc).Take(6).ToList();
        dto.Failed = items.Where(i => i.Status == GrowthStatus.Failed).OrderByDescending(i => i.UpdatedAtUtc).ToList();
        dto.DueManual = items.Where(i => i.ManualPublish && i.Status == GrowthStatus.Scheduled && i.ScheduledAtUtc <= now.AddMinutes(30)).ToList();

        var totalDays = l.Dto.TotalDays;
        var dayNo = Math.Clamp(l.Dto.DayNumber, 0, totalDays);
        var planned = (c.ReelsPerDay + c.PhotosPerDay + c.StoriesPerDay) * totalDays;
        dto.Progress = new GrowthProgressDto
        {
            Day = dayNo, TotalDays = totalDays,
            TimePercent = c.Status is GrowthStatus.Completed ? 100 : c.Status == GrowthStatus.Draft ? 0 : Math.Round(dayNo * 100.0 / Math.Max(1, totalDays), 1),
            Planned = planned, Published = published.Count,
            FollowerTargetPercent = c.TargetFollowers is { } tf && tf > 0 && newFollowers is { } nf ? Math.Round(nf * 100.0 / tf, 1) : null,
            ViewTargetPercent = c.TargetViews is { } tv && tv > 0 && views is { } v ? Math.Round(v * 100.0 / tv, 1) : null
        };
        dto.Insights = Insights(l, dto);
        return dto;
    }

    private static List<GrowthKindStatsDto> ByKind(List<GrowthScheduledContent> published, Loaded l) =>
        GrowthKinds.All.Select(kind =>
        {
            var list = published.Where(i => i.Kind == kind).Select(i => Latest(l, i.Id)).ToList();
            var with = list.Where(m => m != null && (m.Views != null || m.Reactions != null)).ToList();
            var views = SumOrNull(with.Select(m => m!.Views));
            var eng = SumOrNull(with.Select(m => GrowthContentService.Engagement(m!)));
            var viewCount = with.Count(m => m!.Views != null);
            var engCount = with.Count(m => GrowthContentService.Engagement(m!) != null);
            return new GrowthKindStatsDto
            {
                Kind = kind, Published = list.Count, WithMetrics = with.Count, Views = views, Reach = SumOrNull(with.Select(m => m!.Reach)),
                Engagement = eng, Shares = SumOrNull(with.Select(m => m!.Shares)), Comments = SumOrNull(with.Select(m => m!.Comments)),
                AvgViews = views is { } v && viewCount > 0 ? Math.Round((double)v / viewCount, 1) : null,
                AvgEngagement = eng is { } e && engCount > 0 ? Math.Round((double)e / engCount, 1) : null
            };
        }).Where(k => k.Published > 0).ToList();

    /// <summary>Plain observations from the real numbers (no AI, no guessing): only said when the data supports it.</summary>
    private static List<string> Insights(Loaded l, GrowthOverviewDto o)
    {
        var list = new List<string>();
        var c = l.Campaign;
        if (o.Page is { } p && (!p.IsActive || !p.HasToken)) list.Add("Page đã ngắt kết nối — lịch đăng sẽ thất bại cho tới khi kết nối lại ở Cài đặt → Facebook.");
        if (o.Page?.TokenExpiresAtUtc is { } exp && exp < GrowthTime.EndUtc(c.EndDate))
            list.Add($"Mã truy cập của Page hết hạn lúc {GrowthTime.ToLocal(exp):dd/MM HH:mm} — trước khi chiến dịch kết thúc. Nên kết nối lại bằng mã dài hạn.");
        var missing = l.Dto.MetricAvailability.Where(a => !a.Ok).Select(a => a.Metric).ToList();
        if (missing.Count > 0) list.Add($"Meta không trả về {missing.Count} chỉ số cho Page này ({string.Join(", ", missing.Take(4))}). Các ô tương ứng hiển thị \"Chưa có dữ liệu\".");
        if (o.Failed.Count > 0) list.Add($"{o.Failed.Count} nội dung đăng thất bại — xem lý do và bấm Thử lại.");
        if (o.DueManual.Count > 0) list.Add($"{o.DueManual.Count} nội dung đến giờ đăng thủ công.");
        var reels = o.ByKind.FirstOrDefault(k => k.Kind == GrowthKinds.Reel);
        var photos = o.ByKind.Where(k => k.Kind is GrowthKinds.Photo or GrowthKinds.Album).ToList();
        var photoAvg = photos.Sum(k => k.WithMetrics) >= 2 ? photos.Where(k => k.AvgEngagement != null).Select(k => k.AvgEngagement!.Value).DefaultIfEmpty().Average() : 0;
        if (reels is { AvgEngagement: { } re, WithMetrics: >= 2 } && photoAvg > 0)
            list.Add(re >= photoAvg
                ? $"Reels đang có tương tác trung bình {re:0.#}/bài, gấp {re / photoAvg:0.#} lần bài ảnh ({photoAvg:0.#}/bài)."
                : $"Bài ảnh đang có tương tác trung bình {photoAvg:0.#}/bài, cao hơn Reels ({re:0.#}/bài).");
        if (o.Before is { NewFollows: { } bf, DaysWithData: > 0 } && o.During is { NewFollows: { } df, DaysWithData: > 0 } && o.During.DaysWithData > 0)
        {
            var perDayBefore = bf / (double)o.Before.DaysWithData;
            var perDayDuring = df / (double)o.During.DaysWithData;
            list.Add($"Follower mới trung bình/ngày: trước chiến dịch {perDayBefore:0.#}, trong chiến dịch {perDayDuring:0.#} (số liệu Meta; chưa đủ để kết luận nguyên nhân).");
        }
        if (o.Progress.Published == 0 && c.Status == GrowthStatus.Running) list.Add("Chưa có bài nào được đăng — vào Lịch đăng nội dung để lên lịch.");
        return list;
    }

    // ═══════════════════════════════ analytics ═══════════════════════════════

    public async Task<List<GrowthPostAnalyticsDto>> PostsAsync(Guid campaignId, CancellationToken ct)
    {
        var l = await LoadAsync(campaignId, ct);
        return PostRows(l);
    }

    private static List<GrowthPostAnalyticsDto> PostRows(Loaded l) =>
        l.Items.Where(i => i.Status == GrowthStatus.Published).Select(i =>
        {
            var m = Latest(l, i.Id);
            var local = i.PublishedAtUtc is { } p ? GrowthTime.ToLocal(p) : (DateTime?)null;
            var caption = (i.Caption ?? "").Replace('\n', ' ').Trim();
            return new GrowthPostAnalyticsDto
            {
                ContentId = i.Id, Kind = i.Kind, Title = i.Title, CaptionExcerpt = caption.Length > 140 ? caption[..140] + "…" : caption,
                CaptionLength = caption.Length, HashtagCount = GrowthContentService.CountHashtags(i.Caption ?? "", i.Hashtags ?? ""),
                Style = i.Style, Cta = i.Cta, Topic = i.Topic, PublishedAtUtc = i.PublishedAtUtc,
                LocalDate = local?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), LocalTime = local?.ToString("HH:mm", CultureInfo.InvariantCulture),
                Views = m?.Views, Reach = m?.Reach, Reactions = m?.Reactions, Comments = m?.Comments, Shares = m?.Shares,
                Engagement = m == null ? null : GrowthContentService.Engagement(m), Follows = m?.Follows, PermalinkUrl = i.PermalinkUrl,
                PublishedManually = i.PublishedManually,
                MetricError = i.Kind == GrowthKinds.Story ? "Meta không cung cấp số liệu Story của Page qua API." :
                    i.PublishedManually ? "Đăng thủ công — ứng dụng không có mã bài để đọc số liệu." : m?.Error
            };
        }).OrderByDescending(r => r.PublishedAtUtc).ToList();

    public async Task<GrowthRankingDto> RankingAsync(Guid campaignId, CancellationToken ct)
    {
        var rows = (await PostsAsync(campaignId, ct)).Where(r => r.Kind != GrowthKinds.Story).ToList();
        return Rank(rows);
    }

    public static GrowthRankingDto Rank(List<GrowthPostAnalyticsDto> rows)
    {
        List<GrowthPostAnalyticsDto> Top(Func<GrowthPostAnalyticsDto, long?> by) =>
            rows.Where(r => by(r) is > 0).OrderByDescending(r => by(r)).Take(5).ToList();
        var attribution = rows.Any(r => r.Follows != null);
        var withViews = rows.Where(r => r.Views != null).ToList();
        var medianViews = withViews.Count == 0 ? 0 : withViews.Select(r => r.Views!.Value).OrderBy(v => v).ElementAt(withViews.Count / 2);
        return new GrowthRankingDto
        {
            TopViews = Top(r => r.Views), TopReach = Top(r => r.Reach), TopShares = Top(r => r.Shares), TopEngagement = Top(r => r.Engagement),
            TopFollows = Top(r => r.Follows), FollowAttributionAvailable = attribution,
            HighViewsNoFollows = attribution
                ? rows.Where(r => r.Follows == 0 && r.Views is { } v && v > 0 && v >= medianViews).OrderByDescending(r => r.Views).Take(5).ToList()
                : new(),
            Weakest = withViews.Count >= 4 ? withViews.OrderBy(r => r.Views).ThenBy(r => r.Engagement ?? 0).Take(3).ToList() : new()
        };
    }

    public async Task<GrowthHoursDto> HoursAsync(Guid campaignId, CancellationToken ct)
    {
        var l = await LoadAsync(campaignId, ct);
        var samples = PostRows(l).Where(r => r.PublishedAtUtc != null && r.Views != null && !r.PublishedManually)
            .Select(r => (At: r.PublishedAtUtc!.Value, Views: (double)r.Views!.Value, Eng: (double?)r.Engagement)).ToList();
        var basis = $"{samples.Count} bài của chiến dịch";
        // the Page's other Reels (already followed by the app) give more samples of the same audience
        if (l.Campaign.FacebookAccountId is { } pid)
        {
            var linked = l.Items.Where(i => i.FacebookPostRecordId != null).Select(i => i.FacebookPostRecordId!.Value).ToList();
            var since = DateTime.UtcNow.AddDays(-60);
            var settled = DateTime.UtcNow.AddDays(-1);
            var reels = await _db.FacebookPosts.AsNoTracking()
                .Where(p => p.FacebookAccountId == pid && p.UploadStatus == "Published" && p.PublishedAtUtc != null
                            && p.PublishedAtUtc >= since && p.PublishedAtUtc <= settled && !linked.Contains(p.Id))
                .Select(p => new
                {
                    At = p.PublishedAtUtc!.Value,
                    Views = p.Metrics.Where(m => m.Status == "Recorded").Select(m => (long?)m.Views).Max(),
                    Eng = p.Metrics.Where(m => m.Status == "Recorded").Select(m => (long?)(m.Engagement + m.Comments + m.Shares)).Max()
                }).ToListAsync(ct);
            var extra = reels.Where(r => r.Views != null).Select(r => (r.At, (double)r.Views!.Value, (double?)r.Eng)).ToList();
            samples.AddRange(extra);
            if (extra.Count > 0) basis += $" + {extra.Count} Reels khác của Page (60 ngày)";
        }
        return ComputeHours(samples, Math.Max(1, GrowthTime.ParseSlots(l.Campaign.TimeSlots).Count), basis);
    }

    public static GrowthHoursDto ComputeHours(IReadOnlyList<(DateTime At, double Views, double? Eng)> samples, int slotCount, string basis)
    {
        var dto = new GrowthHoursDto { Samples = samples.Count, MinSamples = HourMinSamples, EnoughData = samples.Count >= HourMinSamples, Basis = basis };
        dto.Buckets = samples.GroupBy(s => GrowthTime.ToLocal(s.At).Hour).OrderBy(g => g.Key).Select(g => new GrowthHourBucketDto
        {
            Hour = g.Key, Label = $"{g.Key:00}:00", Posts = g.Count(), AvgViews = Math.Round(g.Average(s => s.Views), 1),
            AvgEngagement = g.Any(s => s.Eng != null) ? Math.Round(g.Where(s => s.Eng != null).Average(s => s.Eng!.Value), 1) : null
        }).ToList();
        if (dto.EnoughData)
            dto.SuggestedSlots = dto.Buckets.Where(b => b.Posts >= 2).OrderByDescending(b => b.AvgViews).Take(slotCount)
                .Select(b => $"{b.Hour:00}:00").OrderBy(s => s, StringComparer.Ordinal).ToList();
        return dto;
    }

    public async Task<GrowthFollowersDto> FollowersAsync(Guid campaignId, CancellationToken ct)
    {
        var l = await LoadAsync(campaignId, ct);
        var o = await OverviewAsync(campaignId, ct);
        var c = l.Campaign;
        var newFollowers = o.Kpis.First(k => k.Key == "new_followers").Value is { } nf ? (long)nf : (long?)null;
        var visits = o.Kpis.First(k => k.Key == "page_visits").Value is { } pv ? (long)pv : (long?)null;
        var dto = new GrowthFollowersDto
        {
            Starting = c.StartingFollowers, Current = o.CurrentFollowers, Gain = newFollowers, GrowthPercent = GrowthPercent(newFollowers, c.StartingFollowers),
            PageVisits = visits, ConversionPercent = ConversionPercent(newFollowers, visits),
            ConversionNote = visits == null ? "Chưa có lượt truy cập Page (Meta không trả về page_views_total hoặc chưa nhập tay)." : null,
            ByKind = o.ByKind,
            InvitesSent = SumOrNull(l.Days.Where(d => d.Date >= c.StartDate).Select(d => d.ManualInvitesSent)),
            InvitesAccepted = SumOrNull(l.Days.Where(d => d.Date >= c.StartDate).Select(d => d.ManualInvitesAccepted))
        };
        long? prev = null;
        foreach (var d in o.Daily)
        {
            var row = l.Days.FirstOrDefault(x => x.Date == d.Date);
            var dayVisits = d.PageViews ?? row?.ManualPageVisits;
            var dayNew = d.NewFollows ?? row?.ManualNewFollowers;
            dto.Days.Add(new GrowthFollowerDayDto
            {
                Date = d.Date, Followers = d.Followers, NewFollows = dayNew, Unfollows = d.Unfollows,
                NetChange = d.Followers != null && prev != null ? d.Followers - prev : null,
                PageVisits = dayVisits, PageVisitsSource = d.PageViews != null ? "meta" : row?.ManualPageVisits != null ? "manual" : null,
                ConversionPercent = ConversionPercent(dayNew, dayVisits), Published = d.Published,
                InvitesSent = row?.ManualInvitesSent, InvitesAccepted = row?.ManualInvitesAccepted
            });
            if (d.Followers != null) prev = d.Followers;
        }
        dto.BestKind = o.ByKind.Where(k => k.WithMetrics >= 2 && k.AvgEngagement != null).OrderByDescending(k => k.AvgEngagement).FirstOrDefault()?.Kind;
        return dto;
    }

    private static string Cut(string s, int max)
    {
        s = GrowthGraphClient.Redact(s);
        return s.Length > max ? s[..max] : s;
    }
}
