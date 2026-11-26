using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.AiStudio;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Infrastructure.Growth;

public sealed record GrowthPublishResult(string PostId, string? VideoId, string? PermalinkUrl, Guid? FacebookPostRecordId)
{
    public string? InstagramMediaId { get; init; }
    public string? InstagramPermalink { get; init; }
    /// <summary>Why the Instagram copy is missing (Facebook is posted either way).</summary>
    public string? InstagramError { get; init; }
}

/// <summary>Posts one calendar item on Facebook. Separate from the scheduler so the scheduling rules can be tested without Meta.</summary>
public interface IGrowthPublishGateway
{
    Task<GrowthPublishResult> PublishAsync(GrowthCampaign campaign, GrowthScheduledContent item, CancellationToken ct);
}

/// <summary>
/// Posting goes through the app's existing functions: Studio Video AI's own publish (<see cref="StudioService.RunPublishAsync"/>),
/// the news Reel publish (<see cref="IFacebookPublishingService.PublishReelAsync"/>), the video / photo / video-Story
/// uploads of <see cref="IFacebookPublishingService"/>. Only photo Stories and text-only posts, which the app had no
/// function for, use <see cref="GrowthGraphClient"/>.
/// </summary>
public class GrowthPublishGateway : IGrowthPublishGateway
{
    private readonly IApplicationDbContext _db;
    private readonly IFacebookPublishingService _facebook;
    private readonly StudioService _studio;
    private readonly AiStudioService _aiStudio;
    private readonly AiStudioMedia _media;
    private readonly GrowthGraphClient _graph;
    private readonly GrowthContentService _content;
    private readonly GrowthMedia _growthMedia;

    public GrowthPublishGateway(IApplicationDbContext db, IFacebookPublishingService facebook, StudioService studio, AiStudioService aiStudio,
        AiStudioMedia media, GrowthGraphClient graph, GrowthContentService content, GrowthMedia growthMedia)
    {
        _growthMedia = growthMedia;
        _db = db;
        _facebook = facebook;
        _studio = studio;
        _aiStudio = aiStudio;
        _media = media;
        _graph = graph;
        _content = content;
    }

    /// <summary>Caption + hashtags (hashtags already in the caption are not repeated).</summary>
    public static string ComposeMessage(string caption, string hashtags)
    {
        var text = (caption ?? "").Trim();
        var extra = (hashtags ?? "").Split(new[] { ' ', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(h => h.StartsWith('#') ? h : "#" + h)
            .Where(h => h.Length > 1 && !text.Contains(h, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return extra.Count == 0 ? text : (text + "\n\n" + string.Join(" ", extra)).Trim();
    }

    public static string PostUrl(string postId) => $"https://www.facebook.com/{postId}";

    public async Task<GrowthPublishResult> PublishAsync(GrowthCampaign campaign, GrowthScheduledContent item, CancellationToken ct)
    {
        try
        {
            var result = await PublishFacebookAsync(campaign, item, ct);
            if (!campaign.AlsoInstagram || campaign.FacebookAccountId is not { } page) return result;
            // Facebook is live: the Instagram copy is extra — its failure is recorded, never thrown
            try
            {
                var ig = await PublishInstagramAsync(page, item, result, ct);
                return ig == null ? result : result with
                {
                    InstagramMediaId = ig.Value.MediaId, InstagramPermalink = ig.Value.MediaId != null ? ig.Value.Permalink ?? "posted" : null, InstagramError = ig.Value.Error
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return result with { InstagramError = Cut(GrowthGraphClient.Redact(ex.Message), 1500) };
            }
        }
        finally
        {
            if (_storyTempDir != null) StudioMedia.TryDelete(_storyTempDir);
            _storyTempDir = null;
            _lastStoryVideo = null;
        }
    }

    private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;

    /// <summary>
    /// The same content on the Page's linked Instagram. Reels of Studio Video AI and news videos are already posted there
    /// by their own publish functions (their result is passed on); text-only posts can't go on Instagram.
    /// </summary>
    private async Task<(string? MediaId, string? Permalink, string? Error)?> PublishInstagramAsync(Guid pageId, GrowthScheduledContent item, GrowthPublishResult fb, CancellationToken ct)
    {
        if (fb.InstagramMediaId != null || fb.InstagramError != null) return (fb.InstagramMediaId, fb.InstagramPermalink, fb.InstagramError);
        var message = ComposeMessage(item.Caption, item.Hashtags);
        var assetIds = GrowthContentService.AssetIds(item.AssetIdsJson);
        switch (item.Kind)
        {
            case GrowthKinds.Reel when item.SourceType == GrowthSources.AiStudioProject && item.SourceId is { } sid:
            {
                var blob = await _db.AiStudioProjects.AsNoTracking().Where(p => p.Id == sid).Select(p => p.FinalBlobId).FirstOrDefaultAsync(ct);
                var file = await _aiStudio.FileAsync(blob, ct) ?? throw new InvalidOperationException("Mất file video để đăng Instagram.");
                var (mediaId, permalink) = await _facebook.PublishInstagramVideoFileAsync(pageId, file.Path, StudioService.CaptionWithDisclosure(message), null, ct);
                if (fb.FacebookPostRecordId is { } rec)
                {
                    // the existing stats read the Instagram numbers of this record
                    var record = await _db.FacebookPosts.FirstOrDefaultAsync(p => p.Id == rec, ct);
                    if (record != null) { record.InstagramMediaId = mediaId; record.InstagramPermalink = permalink; await _db.SaveChangesAsync(ct); }
                }
                return (mediaId, permalink, null);
            }
            case GrowthKinds.Reel:
                return null; // Studio / news publish already handled Instagram (see PublishFacebookAsync)
            case GrowthKinds.Photo or GrowthKinds.Album or GrowthKinds.Interactive:
            {
                if (assetIds.Count == 0) return (null, null, "Instagram không đăng bài chỉ có chữ — bài này chỉ lên Facebook.");
                var urls = new List<string>();
                foreach (var id in assetIds)
                    urls.Add(await _content.PublicAssetUrlAsync(id, ct)
                             ?? throw new InvalidOperationException("Instagram cần đường dẫn công khai tới ảnh — đặt App:PublicBaseUrl hoặc dùng bucket R2."));
                var (mediaId, permalink) = await _graph.PublishInstagramImagesAsync(pageId, urls, message, ct);
                return (mediaId, permalink, null);
            }
            case GrowthKinds.Story:
            {
                if (assetIds.Count == 1 && await _db.GrowthContentAssets.AsNoTracking().AnyAsync(a => a.Id == assetIds[0] && a.Kind == "Image", ct))
                {
                    var url = await _content.PublicAssetUrlAsync(assetIds[0], ct)
                              ?? throw new InvalidOperationException("Instagram cần đường dẫn công khai tới ảnh Tin.");
                    var (mediaId, permalink) = await _graph.PublishInstagramStoryAsync(pageId, url, null, ct);
                    return (mediaId, permalink, null);
                }
                if (_lastStoryVideo == null || !File.Exists(_lastStoryVideo)) return (null, null, "Không còn file video Tin để đăng Instagram.");
                var (vid, vlink) = await _graph.PublishInstagramStoryAsync(pageId, null, _lastStoryVideo, ct);
                return (vid, vlink, null);
            }
            default:
                return null;
        }
    }

    /// <summary>The video file just posted as a Facebook Story (kept until the Instagram copy is done).</summary>
    private string? _lastStoryVideo;
    private string? _storyTempDir;

    private async Task<GrowthPublishResult> PublishFacebookAsync(GrowthCampaign campaign, GrowthScheduledContent item, CancellationToken ct)
    {
        var pageId = campaign.FacebookAccountId ?? throw new GrowthPublishException(GrowthErrorKind.Token, "Page của chiến dịch đã bị xoá khỏi ứng dụng.");
        var message = ComposeMessage(item.Caption, item.Hashtags);
        var assets = await AssetsAsync(item, ct);
        switch (item.Kind)
        {
            case GrowthKinds.Reel:
                return await PublishReelAsync(campaign, item, pageId, message, ct);
            case GrowthKinds.Photo or GrowthKinds.Album:
            {
                if (assets.Count == 0) throw new GrowthPublishException(GrowthErrorKind.Content, "Bài ảnh chưa có ảnh.");
                var postId = await _facebook.PublishPhotoPostAsync(pageId, assets.Select(a => a.Path).ToList(), message, ct);
                return new GrowthPublishResult(postId, null, PostUrl(postId), null);
            }
            case GrowthKinds.Interactive:
            {
                var postId = assets.Count > 0
                    ? await _facebook.PublishPhotoPostAsync(pageId, assets.Select(a => a.Path).ToList(), message, ct)
                    : await _graph.PublishTextPostAsync(pageId, message, ct);
                return new GrowthPublishResult(postId, null, PostUrl(postId), null);
            }
            case GrowthKinds.Story:
                return await PublishStoryAsync(item, pageId, assets, ct);
            default:
                throw new GrowthPublishException(GrowthErrorKind.Content, "Loại nội dung không hợp lệ.");
        }
    }

    private async Task<List<(string Path, string Mime, string Kind)>> AssetsAsync(GrowthScheduledContent item, CancellationToken ct)
    {
        var list = new List<(string, string, string)>();
        foreach (var id in GrowthContentService.AssetIds(item.AssetIdsJson))
        {
            var kind = await _db.GrowthContentAssets.AsNoTracking().Where(a => a.Id == id && a.CampaignId == item.CampaignId).Select(a => a.Kind).FirstOrDefaultAsync(ct);
            var file = kind == null ? null : await _content.AssetFileAsync(id, ct);
            if (file == null) throw new GrowthPublishException(GrowthErrorKind.Content, "Mất tệp đính kèm (đã bị xoá khỏi kho) — hãy tải lại.");
            list.Add((file.Value.Path, file.Value.Mime, kind!));
        }
        return list;
    }

    private async Task<GrowthPublishResult> PublishReelAsync(GrowthCampaign campaign, GrowthScheduledContent item, Guid pageId, string message, CancellationToken ct)
    {
        if (item.SourceId is not { } sourceId) throw new GrowthPublishException(GrowthErrorKind.Content, "Reels chưa chọn video.");
        switch (item.SourceType)
        {
            case GrowthSources.StudioVideo:
            {
                var v = await _db.StudioVideos.FirstOrDefaultAsync(x => x.Id == sourceId, ct)
                        ?? throw new GrowthPublishException(GrowthErrorKind.Content, "Video Studio đã bị xoá.");
                if (v.Status == "Published" || v.PublishedPostId != null)
                    throw new GrowthPublishException(GrowthErrorKind.Content, "Video này đã được đăng từ Studio — không đăng lại để tránh trùng.");
                if (v.FacebookAccountId != null && v.FacebookAccountId != pageId) throw new GrowthPublishException(GrowthErrorKind.Content, "Video này thuộc Page khác.");
                if (v.Status != "Ready" || v.FinalBlobId == null)
                    throw new GrowthPublishException(GrowthErrorKind.Transient, $"Video Studio đang bận ({v.Status}) — sẽ thử lại.");
                // the studio's own publish (Facebook + linked Instagram + stats record), exactly as its "Đăng" button
                v.FacebookAccountId = pageId;
                v.Caption = message;
                v.Status = "Publishing";
                v.Progress = "Đang đăng (Chiến dịch tăng trưởng)…";
                v.ErrorMessage = null;
                await _db.SaveChangesAsync(ct);
                await _studio.RunPublishAsync(v.Id, ct);
                var after = await _db.StudioVideos.AsNoTracking().FirstAsync(x => x.Id == v.Id, ct);
                if (after.Status != "Published" || after.PublishedPostId == null)
                    throw new InvalidOperationException(after.ErrorMessage ?? "Studio không đăng được video.");
                var record = await _db.FacebookPosts.AsNoTracking().Where(p => p.StudioVideoId == v.Id).OrderByDescending(p => p.CreatedAtUtc)
                    .Select(p => new { p.Id, p.FacebookVideoId }).FirstOrDefaultAsync(ct);
                return new GrowthPublishResult(after.PublishedPostId, record?.FacebookVideoId, PostUrl(after.PublishedPostId), record?.Id)
                {
                    // the studio's publish also posts on the linked Instagram: report what happened there
                    InstagramMediaId = after.InstagramPermalink != null ? "studio" : null,
                    InstagramPermalink = after.InstagramPermalink is { } ip && ip != "posted" ? ip : null,
                    InstagramError = after.InstagramPermalink == null ? after.InstagramError : null
                };
            }
            case GrowthSources.AiStudioProject:
            {
                var p = await _db.AiStudioProjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sourceId, ct)
                        ?? throw new GrowthPublishException(GrowthErrorKind.Content, "Dự án AI Video Studio đã bị xoá.");
                if (await _db.GrowthScheduledContents.AnyAsync(x => x.Id != item.Id && x.SourceId == sourceId && x.Status == GrowthStatus.Published && x.Kind == GrowthKinds.Reel, ct))
                    throw new GrowthPublishException(GrowthErrorKind.Content, "Video này đã được đăng Reels trong chiến dịch — không đăng lại để tránh trùng.");
                var file = await _aiStudio.FileAsync(p.FinalBlobId, ct) ?? throw new GrowthPublishException(GrowthErrorKind.Content, "Mất file video của dự án (hết hạn lưu trữ?).");
                var probe = await _media.ProbeAsync(file.Path, ct);
                var caption = StudioService.CaptionWithDisclosure(message); // realistic AI video must be labelled
                var (videoId, postId) = await _facebook.PublishVideoFileAsync(pageId, file.Path, caption, probe.Duration, ct);
                var record = new FacebookPost
                {
                    FacebookAccountId = pageId, Source = "Growth", Title = Trim(p.Title, 300), FacebookVideoId = videoId, FacebookPostId = postId,
                    Status = "Published", UploadStatus = "Published", PublishedAtUtc = DateTime.UtcNow
                };
                _db.FacebookPosts.Add(record);
                await _db.SaveChangesAsync(ct);
                return new GrowthPublishResult(postId, videoId, probe.Duration <= 90 ? $"https://www.facebook.com/reel/{videoId}" : PostUrl(postId), record.Id);
            }
            case GrowthSources.VideoProject:
            {
                // the news publish keeps its own rules: editorial approval required, never posted twice
                var result = await _facebook.PublishReelAsync(sourceId, new PublishReelRequestDto { FacebookAccountId = pageId, CustomCaption = message, PostToInstagram = campaign.AlsoInstagram }, ct);
                if (!string.Equals(result.UploadStatus, "Published", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(result.FacebookPostId))
                    throw new InvalidOperationException(result.ErrorMessage ?? "Đăng video tin tức thất bại.");
                return new GrowthPublishResult(result.FacebookPostId, result.FacebookVideoId, result.PostUrl, result.FacebookPostRecordId)
                {
                    InstagramMediaId = result.InstagramMediaId,
                    InstagramPermalink = result.InstagramPermalink,
                    InstagramError = campaign.AlsoInstagram && result.InstagramMediaId == null ? result.InstagramError ?? "Không đăng được lên Instagram." : null
                };
            }
            default:
                throw new GrowthPublishException(GrowthErrorKind.Content, "Nguồn video không hợp lệ.");
        }
    }

    private async Task<GrowthPublishResult> PublishStoryAsync(GrowthScheduledContent item, Guid pageId, List<(string Path, string Mime, string Kind)> assets, CancellationToken ct)
    {
        string? videoPath = null;
        if (assets.Count == 1 && assets[0].Kind == "Image")
        {
            var id = await _graph.PublishPhotoStoryAsync(pageId, assets[0].Path, assets[0].Mime, ct);
            return new GrowthPublishResult(id, null, null, null);
        }
        if (assets.Count == 1) videoPath = assets[0].Path;
        else if (item.SourceType == GrowthSources.StudioVideo && item.SourceId is { } sv)
        {
            var blob = await _db.StudioVideos.AsNoTracking().Where(v => v.Id == sv).Select(v => v.FinalBlobId).FirstOrDefaultAsync(ct);
            videoPath = blob is { } b ? (await _studio.DiskFileAsync(b, ct))?.Path : null;
        }
        else if (item.SourceType == GrowthSources.AiStudioProject && item.SourceId is { } ap)
        {
            var blob = await _db.AiStudioProjects.AsNoTracking().Where(p => p.Id == ap).Select(p => p.FinalBlobId).FirstOrDefaultAsync(ct);
            videoPath = (await _aiStudio.FileAsync(blob, ct))?.Path;
        }
        if (videoPath == null) throw new GrowthPublishException(GrowthErrorKind.Content, "Không tìm thấy ảnh / video cho Story.");
        if (item.ClipStartSeconds is { } start && item.ClipSeconds is { } length)
        {
            // a highlight of a longer video (autopilot): cut it just before posting (the folder is removed after Instagram)
            _storyTempDir = StudioMedia.TempDir();
            var cut = Path.Combine(_storyTempDir, "story.mp4");
            await _growthMedia.CutAsync(videoPath, start, length, cut, ct);
            videoPath = cut;
        }
        _lastStoryVideo = videoPath;
        return await PublishVideoStoryFileAsync(pageId, videoPath, ct);
    }

    private async Task<GrowthPublishResult> PublishVideoStoryFileAsync(Guid pageId, string videoPath, CancellationToken ct)
    {
        var probe = await _media.ProbeAsync(videoPath, ct);
        if (probe.Duration > GrowthContentService.MaxStorySeconds)
            throw new GrowthPublishException(GrowthErrorKind.Content, $"Video Story dài {probe.Duration:0.#} giây — Facebook chỉ nhận tối đa 60 giây.");
        try
        {
            var storyId = await _facebook.PublishVideoStoryAsync(pageId, videoPath, ct);
            return new GrowthPublishResult(storyId, null, null, null);
        }
        catch (InvalidOperationException ex) when (GrowthGraphClient.ClassifyMessage(ex) is GrowthErrorKind.Unknown or GrowthErrorKind.Content)
        {
            throw new GrowthPublishException(GrowthErrorKind.Unsupported, ex.Message);
        }
    }

    private static string Trim(string s, int max) => s.Length > max ? s[..max] : s;
}

/// <summary>
/// The scheduler (Hangfire, every minute): completes campaigns past their last day, frees items stuck while posting, and
/// posts what is due. An item is claimed with one conditional UPDATE (Scheduled → Publishing), so two workers (PC and
/// Railway) never post it twice; an item that already has a Facebook post id is never posted again. Temporary errors are
/// retried (at most <see cref="GrowthScheduledContent.MaxAttempts"/>, growing waits); token / permission / content errors are not.
/// </summary>
public class GrowthPublisher
{
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(20);
    private const int PerTick = 5;

    private readonly IApplicationDbContext _db;
    private readonly IGrowthPublishGateway _gateway;
    private readonly GrowthCampaignService _campaigns;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<GrowthPublisher> _logger;

    public GrowthPublisher(IApplicationDbContext db, IGrowthPublishGateway gateway, GrowthCampaignService campaigns, IBackgroundJobClient jobs, ILogger<GrowthPublisher> logger)
    {
        _db = db;
        _gateway = gateway;
        _campaigns = campaigns;
        _jobs = jobs;
        _logger = logger;
    }

    /// <summary>Wait before the n-th retry of a temporary error: 2, 4, 8… minutes (max 30).</summary>
    public static TimeSpan Backoff(int attempt) => TimeSpan.FromMinutes(Math.Min(30, 2 * Math.Pow(2, Math.Max(0, attempt - 1))));

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(120)]
    public async Task TickAsync(CancellationToken ct)
    {
        foreach (var id in await _campaigns.CompleteExpiredAsync(ct))
            _jobs.Enqueue<GrowthReportService>(r => r.GenerateAsync(id, false, CancellationToken.None));

        await RecoverStuckAsync(ct);

        var now = DateTime.UtcNow;
        var due = await _db.GrowthScheduledContents.AsNoTracking()
            .Where(x => x.Status == GrowthStatus.Scheduled && !x.ManualPublish && x.ExternalPostId == null
                        && x.ScheduledAtUtc != null && x.ScheduledAtUtc <= now
                        && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now)
                        && _db.GrowthCampaigns.Any(c => c.Id == x.CampaignId && c.Status == GrowthStatus.Running))
            .OrderBy(x => x.ScheduledAtUtc).Select(x => x.Id).Take(PerTick).ToListAsync(ct);
        foreach (var id in due)
        {
            try { await PublishOneAsync(id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Growth: publishing {Id} crashed", id);
            }
        }
    }

    /// <summary>Items left "Publishing" (worker restarted mid-post): marked failed for the owner to check — never posted again automatically.</summary>
    public async Task<int> RecoverStuckAsync(CancellationToken ct)
    {
        var limit = DateTime.UtcNow - StuckAfter;
        var stuck = await _db.GrowthScheduledContents
            .Where(x => x.Status == GrowthStatus.Publishing && (x.PublishStartedAtUtc == null || x.PublishStartedAtUtc < limit)).ToListAsync(ct);
        foreach (var x in stuck)
        {
            x.Status = GrowthStatus.Failed;
            x.ErrorKind = GrowthErrorKind.Unknown;
            x.LastError = "Việc đăng bị gián đoạn giữa chừng. Hãy mở Page kiểm tra bài đã lên chưa: nếu chưa, bấm \"Thử lại\"; nếu đã lên, bấm \"Đã đăng thủ công\".";
            Log(x.CampaignId, x.Id, "Error", "PublishInterrupted", x.LastError, "scheduler");
        }
        if (stuck.Count > 0) await _db.SaveChangesAsync(ct);
        return stuck.Count;
    }

    /// <summary>Claims and posts one item (no-op if it isn't due, already taken, or its campaign isn't running).</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task PublishOneAsync(Guid contentId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var claimed = await _db.GrowthScheduledContents
            .Where(x => x.Id == contentId && x.Status == GrowthStatus.Scheduled && !x.ManualPublish && x.ExternalPostId == null
                        && x.ScheduledAtUtc != null && x.ScheduledAtUtc <= now.AddMinutes(1)
                        && _db.GrowthCampaigns.Any(c => c.Id == x.CampaignId && c.Status == GrowthStatus.Running))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, GrowthStatus.Publishing)
                .SetProperty(x => x.PublishStartedAtUtc, now)
                .SetProperty(x => x.Attempts, x => x.Attempts + 1)
                .SetProperty(x => x.UpdatedAtUtc, now), ct);
        if (claimed == 0) return;

        var item = await _db.GrowthScheduledContents.FirstAsync(x => x.Id == contentId, ct);
        var campaign = await _db.GrowthCampaigns.AsNoTracking().FirstAsync(c => c.Id == item.CampaignId, ct);
        if (item.ScheduledAtUtc >= GrowthTime.EndUtc(campaign.EndDate) || DateTime.UtcNow >= GrowthTime.EndUtc(campaign.EndDate))
        {
            item.Status = GrowthStatus.Cancelled;
            item.LastError = "Đã quá ngày kết thúc chiến dịch — không đăng.";
            Log(item.CampaignId, item.Id, "Warning", "PublishSkipped", item.LastError, "scheduler");
            await _db.SaveChangesAsync(ct);
            return;
        }

        GrowthPublishResult result;
        try
        {
            result = await _gateway.PublishAsync(campaign, item, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            await FailAsync(item, ex, ct);
            return;
        }

        item.Status = GrowthStatus.Published;
        item.PublishedAtUtc = DateTime.UtcNow;
        item.ExternalPostId = Trim(result.PostId, 100);
        item.ExternalVideoId = result.VideoId == null ? null : Trim(result.VideoId, 100);
        item.PermalinkUrl = result.PermalinkUrl == null ? null : Trim(result.PermalinkUrl, 500);
        item.FacebookPostRecordId = result.FacebookPostRecordId;
        item.InstagramMediaId = result.InstagramMediaId is { } im ? Trim(im, 100) : null;
        item.InstagramPermalink = result.InstagramPermalink is { } ipl ? Trim(ipl, 500) : null;
        item.InstagramError = result.InstagramError is { } ie ? Trim(GrowthGraphClient.Redact(ie), 1500) : null;
        item.LastError = null;
        item.ErrorKind = null;
        item.NextAttemptAtUtc = null;
        Log(item.CampaignId, item.Id, "Info", "Published",
            $"Đã đăng {GrowthContentService.KindLabel(item.Kind)} \"{GrowthContentService.Short(item.Title, item.Caption)}\" (lần thử {item.Attempts})"
            + (item.InstagramMediaId != null ? " · cả Instagram." : item.InstagramError != null ? $" · Instagram chưa đăng được: {item.InstagramError}" : "."), "scheduler");
        // the post is live: this save must not be lost to a cancelled request
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task FailAsync(GrowthScheduledContent item, Exception ex, CancellationToken ct)
    {
        var kind = GrowthGraphClient.ClassifyMessage(ex);
        var clean = GrowthGraphClient.Redact(ex.Message); // the old publishing service keeps Meta's text, which can echo the token
        var message = clean.Length > 1500 ? clean[..1500] : clean;
        item.ErrorKind = kind;
        if (kind == GrowthErrorKind.Transient && item.Attempts < item.MaxAttempts)
        {
            var wait = Backoff(item.Attempts);
            item.Status = GrowthStatus.Scheduled;
            item.NextAttemptAtUtc = DateTime.UtcNow + wait;
            item.LastError = $"Lỗi tạm thời, sẽ thử lại sau {wait.TotalMinutes:0} phút (lần {item.Attempts}/{item.MaxAttempts}). {message}";
            Log(item.CampaignId, item.Id, "Warning", "PublishRetry", item.LastError, "scheduler");
        }
        else
        {
            item.Status = GrowthStatus.Failed;
            item.NextAttemptAtUtc = null;
            item.LastError = kind switch
            {
                GrowthErrorKind.Token => "Mã truy cập của Page hết hạn hoặc bị thu hồi — vào Cài đặt → Facebook để kết nối lại Page, rồi bấm Thử lại. " + message,
                GrowthErrorKind.Permission => "Page / ứng dụng thiếu quyền cho thao tác này — cấp lại quyền (pages_manage_posts, pages_read_engagement…) khi kết nối Page. " + message,
                GrowthErrorKind.Unsupported => "Facebook không cho ứng dụng đăng nội dung này lên Page — hãy đăng thủ công rồi bấm \"Đã đăng thủ công\". " + message,
                _ => message
            };
            Log(item.CampaignId, item.Id, "Error", "PublishFailed", item.LastError, "scheduler");
        }
        _logger.LogWarning("Growth: publishing {Id} failed ({Kind}): {Message}", item.Id, kind, message);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private static string Trim(string s, int max) => s.Length > max ? s[..max] : s;

    private void Log(Guid campaignId, Guid? contentId, string level, string action, string message, string actor) =>
        _db.GrowthActivityLogs.Add(new GrowthActivityLog
        {
            CampaignId = campaignId, ContentId = contentId, Level = level, Action = action,
            Message = message.Length > 2000 ? message[..2000] : message, Actor = actor
        });
}
