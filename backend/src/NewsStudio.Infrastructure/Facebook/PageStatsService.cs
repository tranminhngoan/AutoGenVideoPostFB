using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Trending;

namespace NewsStudio.Infrastructure.Facebook;

/// <summary>
/// Views per Facebook Page. Each published Reel keeps one metrics snapshot per Vietnam day (its lifetime
/// numbers at the last read that day), so the views a page gained on a day are the differences between
/// consecutive snapshots of its videos.
/// </summary>
public class PageStatsService : IPageStatsService
{
    private const string SyncStateKey = "facebook-stats-sync";
    /// <summary>Reels older than this are no longer read (their numbers barely move).</summary>
    private static readonly TimeSpan TrackFor = TimeSpan.FromDays(90);
    private const int MaxPostsPerSync = 120;
    private static readonly SemaphoreSlim SyncLock = new(1, 1);

    private readonly IApplicationDbContext _context;
    private readonly IFacebookPublishingService _facebook;
    private readonly ILogger<PageStatsService> _logger;

    public PageStatsService(IApplicationDbContext context, IFacebookPublishingService facebook, ILogger<PageStatsService> logger)
    {
        _context = context;
        _facebook = facebook;
        _logger = logger;
    }

    private sealed class PageSyncState
    {
        public DateTime? LastSyncUtc { get; set; }
        public string? Error { get; set; }
        public DateTime? InstagramLastSyncUtc { get; set; }
        public string? InstagramError { get; set; }
        /// <summary>Last look for posts made outside the app and for the follower counts.</summary>
        public DateTime? DiscoveredUtc { get; set; }
    }

    /// <summary>
    /// Finds the page's videos the app doesn't know yet (posted by hand on Facebook or Instagram) so their numbers are
    /// followed too, and stores today's follower counts. At most once an hour per page (every time when forced).
    /// </summary>
    private async Task DiscoverAsync(Dictionary<Guid, PageSyncState> state, bool force, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now.AddHours(7));
        var accounts = await _context.FacebookAccounts.AsNoTracking().Where(a => a.IsActive).ToListAsync(ct);
        foreach (var account in accounts)
        {
            var s = state.TryGetValue(account.Id, out var existing) ? existing : state[account.Id] = new PageSyncState();
            if (!force && s.DiscoveredUtc != null && now - s.DiscoveredUtc.Value < TimeSpan.FromHours(1)) continue;
            s.DiscoveredUtc = now;
            try
            {
                var found = await _facebook.ListRecentPagePostsAsync(account.Id, now - TrackFor, ct);
                var fbIds = found.Where(f => f.Network == "facebook").Select(f => f.Id).ToList();
                var igIds = found.Where(f => f.Network == "instagram").Select(f => f.Id).ToList();
                var knownFb = await _context.FacebookPosts.Where(p => p.FacebookVideoId != null && fbIds.Contains(p.FacebookVideoId)).Select(p => p.FacebookVideoId!).ToListAsync(ct);
                var knownIg = await _context.FacebookPosts.Where(p => p.InstagramMediaId != null && igIds.Contains(p.InstagramMediaId)).Select(p => p.InstagramMediaId!).ToListAsync(ct);
                foreach (var f in found)
                {
                    if (f.Network == "facebook" ? knownFb.Contains(f.Id) : knownIg.Contains(f.Id)) continue;
                    var title = string.IsNullOrWhiteSpace(f.Text) ? (f.Network == "facebook" ? "Video trên Facebook" : "Reel trên Instagram") : f.Text.Trim();
                    _context.FacebookPosts.Add(new FacebookPost
                    {
                        FacebookAccountId = account.Id, Source = "Discovered", Title = title.Length > 300 ? title[..300] : title,
                        Status = "Published", UploadStatus = "Published", PublishedAtUtc = f.CreatedUtc,
                        FacebookVideoId = f.Network == "facebook" ? f.Id : null,
                        FacebookPostId = f.Network == "facebook" ? $"{account.PageId}_{f.Id}" : null,
                        InstagramMediaId = f.Network == "instagram" ? f.Id : null,
                        InstagramPermalink = f.Network == "instagram" ? f.Permalink : null
                    });
                }
                var (facebook, instagram) = await _facebook.ReadFollowersAsync(account.Id, ct);
                if (facebook != null || instagram != null)
                {
                    var snap = await _context.PageFollowerSnapshots.FirstOrDefaultAsync(x => x.FacebookAccountId == account.Id && x.Date == today, ct);
                    if (snap == null)
                    {
                        snap = new PageFollowerSnapshot { FacebookAccountId = account.Id, Date = today };
                        _context.PageFollowerSnapshots.Add(snap);
                    }
                    if (facebook != null) snap.FacebookFollowers = facebook.Value;
                    if (instagram != null) snap.InstagramFollowers = instagram;
                    snap.CollectedAtUtc = now;
                }
                await _context.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Discovering posts of page {Page} failed: {Message}", account.PageName, ex.Message);
            }
        }
    }

    /// <summary>How long a Reel's numbers stay fresh enough: new videos change fast, old ones slowly.</summary>
    public static TimeSpan RefreshInterval(TimeSpan age) =>
        age < TimeSpan.FromDays(2) ? TimeSpan.FromHours(1)
        : age < TimeSpan.FromDays(7) ? TimeSpan.FromHours(3)
        : age < TimeSpan.FromDays(30) ? TimeSpan.FromHours(12)
        : TimeSpan.FromHours(24);

    /* ───────────────────────────── Sync ───────────────────────────── */

    public async Task<PageStatsSyncResultDto> SyncAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var result = new PageStatsSyncResultDto();
        if (!await SyncLock.WaitAsync(0, cancellationToken))
        {
            result.Errors.Add("Đang cập nhật số liệu, thử lại sau ít phút.");
            return result;
        }
        try
        {
            var now = DateTime.UtcNow;
            var since = now - TrackFor;
            var discoveryState = await ReadStateAsync(cancellationToken);
            await DiscoverAsync(discoveryState, force, cancellationToken);
            await SaveStateAsync(discoveryState, cancellationToken);
            var posts = await _context.FacebookPosts.AsNoTracking()
                .Where(p => p.Status == "Published" && p.FacebookAccountId != null && p.FacebookVideoId != null
                            && p.PublishedAtUtc != null && p.PublishedAtUtc >= since
                            && p.FacebookAccount!.IsActive)
                .Select(p => new
                {
                    p.Id,
                    PageId = p.FacebookAccountId!.Value,
                    PublishedAtUtc = p.PublishedAtUtc!.Value,
                    LastRead = p.Metrics.Where(m => m.Status == "Recorded").Max(m => (DateTime?)m.CollectedAtUtc)
                })
                .ToListAsync(cancellationToken);

            var due = posts
                .Where(p => p.LastRead == null
                            || now - p.LastRead.Value >= (force ? TimeSpan.FromMinutes(15) : RefreshInterval(now - p.PublishedAtUtc)))
                .OrderBy(p => p.LastRead ?? DateTime.MinValue) // never-read and stalest first
                .Take(MaxPostsPerSync)
                .ToList();
            var state = await ReadStateAsync(cancellationToken);
            foreach (var page in due.GroupBy(p => p.PageId))
            {
                string? error = null;
                var updated = 0;
                foreach (var post in page)
                {
                    result.PostsChecked++;
                    try
                    {
                        await _facebook.FetchMetricsAsync(post.Id, cancellationToken);
                        updated++;
                    }
                    catch (InvalidOperationException ex)
                    {
                        error = ex.Message;
                        // A token without the insights permission fails the same way for every video: stop here
                        if (ex.Message.Contains("read_insights", StringComparison.Ordinal)) break;
                    }
                }
                result.PostsUpdated += updated;
                var s = state.TryGetValue(page.Key, out var existing) ? existing : state[page.Key] = new PageSyncState();
                if (updated > 0) s.LastSyncUtc = DateTime.UtcNow;
                s.Error = updated > 0 && error != null ? null : error;
                if (error != null) result.Errors.Add(error);
            }
            // Instagram Reels (posted together with the Facebook Reel), same refresh rhythm
            var igPosts = await _context.FacebookPosts.AsNoTracking()
                .Where(p => p.Status == "Published" && p.FacebookAccountId != null && p.InstagramMediaId != null
                            && p.PublishedAtUtc != null && p.PublishedAtUtc >= since && p.FacebookAccount!.IsActive)
                .Select(p => new
                {
                    p.Id,
                    PageId = p.FacebookAccountId!.Value,
                    PublishedAtUtc = p.PublishedAtUtc!.Value,
                    LastRead = p.InstagramMetrics.Max(m => (DateTime?)m.CollectedAtUtc)
                })
                .ToListAsync(cancellationToken);
            var igDue = igPosts
                .Where(p => p.LastRead == null
                            || now - p.LastRead.Value >= (force ? TimeSpan.FromMinutes(15) : RefreshInterval(now - p.PublishedAtUtc)))
                .OrderBy(p => p.LastRead ?? DateTime.MinValue)
                .Take(MaxPostsPerSync)
                .ToList();
            foreach (var page in igDue.GroupBy(p => p.PageId))
            {
                string? error = null;
                var updated = 0;
                foreach (var post in page)
                {
                    result.PostsChecked++;
                    try
                    {
                        await _facebook.FetchInstagramMetricsAsync(post.Id, cancellationToken);
                        updated++;
                    }
                    catch (InvalidOperationException ex)
                    {
                        error = ex.Message;
                        if (ex.Message.Contains("instagram_manage_insights", StringComparison.Ordinal)) break;
                    }
                }
                result.PostsUpdated += updated;
                var s = state.TryGetValue(page.Key, out var existing) ? existing : state[page.Key] = new PageSyncState();
                if (updated > 0) s.InstagramLastSyncUtc = DateTime.UtcNow;
                s.InstagramError = updated > 0 && error != null ? null : error;
                if (error != null) result.Errors.Add(error);
            }

            if (result.PostsChecked > 0) await SaveStateAsync(state, cancellationToken);
            _logger.LogInformation("Stats sync: {Updated}/{Checked} Reels updated (Facebook + Instagram)", result.PostsUpdated, result.PostsChecked);
        }
        finally
        {
            SyncLock.Release();
        }
        return result;
    }

    /* ───────────────────────────── Overview ───────────────────────────── */

    public async Task<PageStatsOverviewDto> GetOverviewAsync(int days = 14, CancellationToken cancellationToken = default)
    {
        days = Math.Clamp(days, 7, 90);
        var nowUtc = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(nowUtc.AddHours(7));
        var dates = Enumerable.Range(0, days).Select(i => today.AddDays(i - days + 1)).ToList();
        var dateIndex = dates.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);

        var accounts = await _context.FacebookAccounts.AsNoTracking().OrderBy(a => a.CreatedAtUtc).ToListAsync(cancellationToken);
        var topics = (await _context.AutoPostSettings.AsNoTracking()
                .Where(s => s.FacebookAccountId != null)
                .Select(s => new { Id = s.FacebookAccountId!.Value, s.Topic })
                .ToListAsync(cancellationToken))
            .GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().Topic);
        var posts = await _context.FacebookPosts.AsNoTracking()
            .Where(p => p.Status == "Published" && p.FacebookAccountId != null)
            .Select(p => new
            {
                p.Id,
                PageId = p.FacebookAccountId!.Value,
                p.VideoProjectId,
                Title = p.VideoProject != null ? p.VideoProject.Title : p.Title,
                p.Source,
                p.FacebookVideoId,
                p.PublishedAtUtc,
                p.InstagramMediaId,
                p.InstagramPermalink,
                Metrics = p.Metrics.Select(m => new { m.Status, m.CollectedAtUtc, m.Views, m.Reach, m.Engagement, m.Comments, m.Shares }).ToList(),
                Instagram = p.InstagramMetrics.Select(m => new { m.CollectedAtUtc, m.Views, m.Likes, m.Comments }).ToList()
            })
            .ToListAsync(cancellationToken);
        var state = await ReadStateAsync(cancellationToken);
        var followers = await _context.PageFollowerSnapshots.AsNoTracking()
            .Where(f => f.Date >= today.AddDays(-30)).ToListAsync(cancellationToken);

        var overview = new PageStatsOverviewDto
        {
            Days = days,
            Dates = dates.Select(d => d.ToString("yyyy-MM-dd")).ToList(),
            DailyViews = new List<long>(new long[days])
        };

        foreach (var account in accounts)
        {
            var pagePosts = posts.Where(p => p.PageId == account.Id).ToList();
            if (!account.IsActive && pagePosts.Count == 0) continue;

            topics.TryGetValue(account.Id, out var topic);
            state.TryGetValue(account.Id, out var sync);
            var dto = new PageViewStatsDto
            {
                FacebookAccountId = account.Id,
                PageName = account.PageName,
                Topic = topic,
                TopicName = topic != null ? NewsTopics.NameOf(topic) : null,
                IsActive = account.IsActive,
                Videos = pagePosts.Count(p => p.FacebookVideoId != null),
                DailyViews = new List<long>(new long[days]),
                LastSyncedUtc = sync?.LastSyncUtc,
                SyncError = sync?.Error,
                InstagramUsername = account.InstagramUsername,
                InstagramDailyViews = new List<long>(new long[days]),
                InstagramSyncError = sync?.InstagramError
            };

            long bestViews = -1;
            foreach (var post in pagePosts)
            {
                var recorded = post.Metrics.Where(m => m.Status == "Recorded").ToList();
                if (recorded.Count > 0) dto.VideosWithStats++;
                var latest = post.Metrics.OrderByDescending(m => m.CollectedAtUtc).FirstOrDefault();
                if (latest != null)
                {
                    var views = post.Metrics.Max(m => m.Views);
                    dto.TotalViews += views;
                    dto.Reach += post.Metrics.Max(m => m.Reach);
                    dto.Likes += post.Metrics.Max(m => m.Engagement);
                    dto.Comments += post.Metrics.Max(m => m.Comments);
                    dto.Shares += post.Metrics.Max(m => m.Shares);
                    if (views > bestViews && views > 0)
                    {
                        bestViews = views;
                        dto.TopVideo = new PageTopVideoDto
                        {
                            VideoProjectId = post.VideoProjectId,
                            Title = post.Title ?? "(không tên)",
                            PostUrl = string.IsNullOrWhiteSpace(post.FacebookVideoId) ? null : $"https://www.facebook.com/reel/{post.FacebookVideoId}",
                            Views = views,
                            PublishedAtUtc = post.PublishedAtUtc
                        };
                    }
                }

                // Views gained per day = growth between the video's end-of-day snapshots (it starts at 0 when published)
                long previous = 0;
                foreach (var day in post.Metrics
                             .GroupBy(m => DateOnly.FromDateTime(m.CollectedAtUtc.AddHours(7)))
                             .OrderBy(g => g.Key))
                {
                    var value = Math.Max(previous, day.Max(m => m.Views));
                    var gained = value - previous;
                    previous = value;
                    if (gained <= 0) continue;
                    if (dateIndex.TryGetValue(day.Key, out var i)) dto.DailyViews[i] += gained;
                    if (day.Key == today) dto.ViewsToday += gained;
                    if (day.Key > today.AddDays(-7)) dto.Views7Days += gained;
                    else if (day.Key > today.AddDays(-14)) dto.ViewsPrevious7Days += gained;
                }
            }
            dto.AverageViewsPerVideo = dto.VideosWithStats > 0 ? dto.TotalViews / dto.VideosWithStats : 0;

            // followers now and a week ago
            var snaps = followers.Where(f => f.FacebookAccountId == account.Id).OrderBy(f => f.Date).ToList();
            if (snaps.Count > 0)
            {
                var latest = snaps[^1];
                var weekAgo = snaps.LastOrDefault(f => f.Date <= today.AddDays(-7)) ?? snaps[0];
                dto.Followers = latest.FacebookFollowers;
                dto.FollowersChange7Days = latest.FacebookFollowers - weekAgo.FacebookFollowers;
                dto.InstagramFollowers = latest.InstagramFollowers;
                dto.InstagramFollowersChange7Days = latest.InstagramFollowers != null && weekAgo.InstagramFollowers != null
                    ? latest.InstagramFollowers - weekAgo.InstagramFollowers : null;
            }

            // the latest posts with their numbers
            dto.RecentPosts = pagePosts.OrderByDescending(p => p.PublishedAtUtc).Take(15).Select(p => new PagePostStatsDto
            {
                Title = p.Title ?? "(không tên)",
                Source = p.Source,
                PublishedAtUtc = p.PublishedAtUtc,
                FacebookUrl = string.IsNullOrWhiteSpace(p.FacebookVideoId) ? null : $"https://www.facebook.com/reel/{p.FacebookVideoId}",
                InstagramUrl = p.InstagramPermalink,
                Views = p.Metrics.Count > 0 ? p.Metrics.Max(m => m.Views) : null,
                Likes = p.Metrics.Count > 0 ? p.Metrics.Max(m => m.Engagement) : null,
                Comments = p.Metrics.Count > 0 ? p.Metrics.Max(m => m.Comments) : null,
                Shares = p.Metrics.Count > 0 ? p.Metrics.Max(m => m.Shares) : null,
                InstagramViews = p.Instagram.Count > 0 ? p.Instagram.Max(m => m.Views) : null,
                InstagramLikes = p.Instagram.Count > 0 ? p.Instagram.Max(m => m.Likes) : null,
                InstagramComments = p.Instagram.Count > 0 ? p.Instagram.Max(m => m.Comments) : null
            }).ToList();

            // Instagram: same day-by-day gains from its own snapshots
            foreach (var post in pagePosts.Where(p => p.InstagramMediaId != null))
            {
                dto.InstagramVideos++;
                if (post.Instagram.Count == 0) continue;
                dto.InstagramViews += post.Instagram.Max(m => m.Views);
                dto.InstagramLikes += post.Instagram.Max(m => m.Likes);
                dto.InstagramComments += post.Instagram.Max(m => m.Comments);
                long previous = 0;
                foreach (var day in post.Instagram.GroupBy(m => DateOnly.FromDateTime(m.CollectedAtUtc.AddHours(7))).OrderBy(g => g.Key))
                {
                    var value = Math.Max(previous, day.Max(m => m.Views));
                    var gained = value - previous;
                    previous = value;
                    if (gained <= 0) continue;
                    if (dateIndex.TryGetValue(day.Key, out var i)) dto.InstagramDailyViews[i] += gained;
                    if (day.Key == today) dto.InstagramViewsToday += gained;
                    if (day.Key > today.AddDays(-7)) dto.InstagramViews7Days += gained;
                }
            }

            for (var i = 0; i < days; i++) overview.DailyViews[i] += dto.DailyViews[i];
            overview.Totals.Videos += dto.Videos;
            overview.Totals.TotalViews += dto.TotalViews;
            overview.Totals.ViewsToday += dto.ViewsToday;
            overview.Totals.Views7Days += dto.Views7Days;
            overview.Totals.ViewsPrevious7Days += dto.ViewsPrevious7Days;
            overview.Totals.InstagramVideos += dto.InstagramVideos;
            overview.Totals.InstagramViews += dto.InstagramViews;
            overview.Totals.InstagramViewsToday += dto.InstagramViewsToday;
            overview.Totals.InstagramViews7Days += dto.InstagramViews7Days;
            overview.Totals.Followers += dto.Followers ?? 0;
            overview.Totals.FollowersChange7Days += dto.FollowersChange7Days ?? 0;
            overview.Totals.InstagramFollowers += dto.InstagramFollowers ?? 0;
            overview.Totals.InstagramFollowersChange7Days += dto.InstagramFollowersChange7Days ?? 0;
            overview.Pages.Add(dto);
        }

        overview.Pages = overview.Pages.OrderByDescending(p => p.Views7Days).ThenByDescending(p => p.TotalViews).ToList();
        overview.LastSyncUtc = state.Values.Max(s => s.LastSyncUtc);
        return overview;
    }

    /* ───────────────────────────── State ───────────────────────────── */

    private async Task<Dictionary<Guid, PageSyncState>> ReadStateAsync(CancellationToken ct)
    {
        var value = await _context.SystemSettings.AsNoTracking()
            .Where(s => s.SettingKey == SyncStateKey).Select(s => s.SettingValue).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(value)) return new Dictionary<Guid, PageSyncState>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<Guid, PageSyncState>>(value) ?? new Dictionary<Guid, PageSyncState>();
        }
        catch (JsonException)
        {
            return new Dictionary<Guid, PageSyncState>();
        }
    }

    private async Task SaveStateAsync(Dictionary<Guid, PageSyncState> state, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(state);
        var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == SyncStateKey, ct);
        if (setting == null)
        {
            _context.SystemSettings.Add(new SystemSetting { SettingKey = SyncStateKey, SettingValue = json, Group = "Facebook" });
        }
        else
        {
            setting.SettingValue = json;
            setting.UpdatedAtUtc = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync(ct);
    }
}
