using System.Text.Json;
using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Domain.Enums;
using NewsStudio.Infrastructure.AiStudio;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Infrastructure.Growth;

public static class GrowthSources
{
    public const string StudioVideo = "StudioVideo";
    public const string AiStudioProject = "AiStudioProject";
    public const string VideoProject = "VideoProject";
    public static readonly string[] All = { StudioVideo, AiStudioProject, VideoProject };

    public static string Label(string type) => type switch
    {
        StudioVideo => "Studio Video AI",
        AiStudioProject => "AI Video Studio",
        VideoProject => "Video tin tức",
        _ => type
    };

    public static string? PreviewUrl(string type, Guid id) => type switch
    {
        StudioVideo => $"/api/studio/videos/{id}/final",
        AiStudioProject => $"/api/ai-studio/projects/{id}/final",
        _ => null
    };
}

/// <summary>
/// The campaign calendar: content items (Reels from the existing video tools, pictures, Stories, discussion posts),
/// their approval flow, uploads, and the finished videos that can be scheduled. Every item and file is looked up
/// through its campaign, so an id from another campaign is "not found".
/// </summary>
public partial class GrowthContentService
{
    public const long MaxImageBytes = 8L * 1024 * 1024;
    public const long MaxVideoBytes = 100L * 1024 * 1024;
    public const int MaxCaption = 5000;
    public const int MaxHashtags = 10;
    public const int MaxAlbumImages = 10;
    public const double MaxStorySeconds = 60;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly GrowthCampaignService _campaigns;
    private readonly StudioFileStore _files;
    private readonly AiStudioMedia _media;
    private readonly IBackgroundJobClient _jobs;
    private readonly GrowthMedia _growthMedia;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

    public GrowthContentService(IApplicationDbContext db, GrowthCampaignService campaigns, StudioFileStore files, AiStudioMedia media, IBackgroundJobClient jobs,
        GrowthMedia growthMedia, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _growthMedia = growthMedia;
        _configuration = configuration;
        _db = db;
        _campaigns = campaigns;
        _files = files;
        _media = media;
        _jobs = jobs;
    }

    // ───────────────────────── mapping ─────────────────────────

    public static List<Guid> AssetIds(string json)
    {
        try { return JsonSerializer.Deserialize<List<Guid>>(json, Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static GrowthAssetDto AssetDto(GrowthContentAsset a) => new()
    {
        Id = a.Id, Kind = a.Kind, FileName = a.FileName, MimeType = a.MimeType, SizeBytes = a.SizeBytes, Width = a.Width, Height = a.Height,
        DurationSeconds = a.DurationSeconds, Source = a.Source, Url = $"/api/growth/assets/{a.Id}/file", CreatedAtUtc = a.CreatedAtUtc
    };

    public static GrowthPostMetricDto MetricDto(GrowthPostMetric m) => new()
    {
        Date = m.Date, Views = m.Views, Reach = m.Reach, Reactions = m.Reactions, Comments = m.Comments, Shares = m.Shares, Clicks = m.Clicks,
        Follows = m.Follows, Engagement = Engagement(m), CollectedAtUtc = m.CollectedAtUtc, Error = m.Error
    };

    public static long? Engagement(GrowthPostMetric m) =>
        m.Reactions == null && m.Comments == null && m.Shares == null ? null : (m.Reactions ?? 0) + (m.Comments ?? 0) + (m.Shares ?? 0);

    /// <summary>DTOs for several items at once (assets, source titles and latest numbers loaded in bulk).</summary>
    public async Task<List<GrowthContentDto>> ToDtosAsync(IReadOnlyList<GrowthScheduledContent> items, CancellationToken ct)
    {
        if (items.Count == 0) return new();
        var assetIds = items.SelectMany(i => AssetIds(i.AssetIdsJson)).Distinct().ToList();
        var assets = await _db.GrowthContentAssets.AsNoTracking().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var ids = items.Select(i => i.Id).ToList();
        var metrics = await _db.GrowthPostMetrics.AsNoTracking().Where(m => ids.Contains(m.ContentId))
            .GroupBy(m => m.ContentId).Select(g => g.OrderByDescending(m => m.Date).First()).ToListAsync(ct);
        var titles = await SourceTitlesAsync(items, ct);
        return items.Select(i => new GrowthContentDto
        {
            Id = i.Id, CampaignId = i.CampaignId, Kind = i.Kind, Status = i.Status, Title = i.Title, Caption = i.Caption, Hashtags = i.Hashtags,
            Style = i.Style, Cta = i.Cta, Topic = i.Topic, SourceType = i.SourceType, SourceId = i.SourceId,
            SourceTitle = i.SourceType != null && i.SourceId is { } sid ? titles.GetValueOrDefault((i.SourceType, sid)) : null,
            SourcePreviewUrl = i.SourceType != null && i.SourceId is { } pid ? GrowthSources.PreviewUrl(i.SourceType, pid) : null,
            Assets = AssetIds(i.AssetIdsJson).Where(assets.ContainsKey).Select(a => AssetDto(assets[a])).ToList(),
            ScheduledAtUtc = i.ScheduledAtUtc, ManualPublish = i.ManualPublish, Attempts = i.Attempts, MaxAttempts = i.MaxAttempts,
            NextAttemptAtUtc = i.NextAttemptAtUtc, LastError = i.LastError, ErrorKind = i.ErrorKind, PublishedAtUtc = i.PublishedAtUtc,
            ExternalPostId = i.ExternalPostId, PermalinkUrl = i.PermalinkUrl, PublishedManually = i.PublishedManually,
            ClipStartSeconds = i.ClipStartSeconds, ClipSeconds = i.ClipSeconds, ScriptId = i.ScriptId,
            InstagramMediaId = i.InstagramMediaId, InstagramPermalink = i.InstagramPermalink, InstagramError = i.InstagramError,
            LatestMetric = metrics.FirstOrDefault(m => m.ContentId == i.Id) is { } m ? MetricDto(m) : null,
            CreatedAtUtc = i.CreatedAtUtc, UpdatedAtUtc = i.UpdatedAtUtc
        }).ToList();
    }

    private async Task<Dictionary<(string, Guid), string>> SourceTitlesAsync(IReadOnlyList<GrowthScheduledContent> items, CancellationToken ct)
    {
        var result = new Dictionary<(string, Guid), string>();
        List<Guid> Of(string type) => items.Where(i => i.SourceType == type && i.SourceId != null).Select(i => i.SourceId!.Value).Distinct().ToList();
        var studio = Of(GrowthSources.StudioVideo);
        if (studio.Count > 0)
            foreach (var v in await _db.StudioVideos.AsNoTracking().Where(v => studio.Contains(v.Id)).Select(v => new { v.Id, v.Title }).ToListAsync(ct))
                result[(GrowthSources.StudioVideo, v.Id)] = v.Title;
        var ai = Of(GrowthSources.AiStudioProject);
        if (ai.Count > 0)
            foreach (var p in await _db.AiStudioProjects.AsNoTracking().Where(p => ai.Contains(p.Id)).Select(p => new { p.Id, p.Title }).ToListAsync(ct))
                result[(GrowthSources.AiStudioProject, p.Id)] = p.Title;
        var news = Of(GrowthSources.VideoProject);
        if (news.Count > 0)
            foreach (var p in await _db.VideoProjects.AsNoTracking().Where(p => news.Contains(p.Id)).Select(p => new { p.Id, p.Title }).ToListAsync(ct))
                result[(GrowthSources.VideoProject, p.Id)] = p.Title;
        return result;
    }

    // ───────────────────────── reading ─────────────────────────

    public async Task<GrowthScheduledContent> LoadAsync(Guid campaignId, Guid contentId, CancellationToken ct) =>
        await _db.GrowthScheduledContents.FirstOrDefaultAsync(x => x.Id == contentId && x.CampaignId == campaignId, ct)
        ?? throw new KeyNotFoundException("Không tìm thấy nội dung trong chiến dịch này.");

    public async Task<GrowthContentDto> GetAsync(Guid campaignId, Guid contentId, CancellationToken ct)
    {
        var item = await _db.GrowthScheduledContents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == contentId && x.CampaignId == campaignId, ct)
                   ?? throw new KeyNotFoundException("Không tìm thấy nội dung trong chiến dịch này.");
        return (await ToDtosAsync(new[] { item }, ct))[0];
    }

    public async Task<List<GrowthContentDto>> ListAsync(Guid campaignId, DateTime? fromUtc, DateTime? toUtc, string? kind, string? status, CancellationToken ct)
    {
        await _campaigns.LoadAsync(campaignId, ct);
        var q = _db.GrowthScheduledContents.AsNoTracking().Where(x => x.CampaignId == campaignId);
        if (fromUtc is { } f) q = q.Where(x => x.ScheduledAtUtc == null || x.ScheduledAtUtc >= f || x.PublishedAtUtc >= f);
        if (toUtc is { } t) q = q.Where(x => x.ScheduledAtUtc == null || x.ScheduledAtUtc < t);
        if (!string.IsNullOrWhiteSpace(kind)) q = q.Where(x => x.Kind == kind);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        var items = await q.OrderBy(x => x.ScheduledAtUtc == null).ThenBy(x => x.ScheduledAtUtc).ThenBy(x => x.CreatedAtUtc).Take(500).ToListAsync(ct);
        return await ToDtosAsync(items, ct);
    }

    // ───────────────────────── create / edit ─────────────────────────

    public async Task<GrowthContentDto> CreateAsync(Guid campaignId, GrowthContentInputDto input, CancellationToken ct)
    {
        var c = await _campaigns.LoadAsync(campaignId, ct);
        var key = (input.IdempotencyKey ?? "").Trim();
        if (key.Length > 100) throw new ArgumentException("Idempotency key quá dài.");
        if (key.Length > 0)
        {
            var existing = await _db.GrowthScheduledContents.AsNoTracking().FirstOrDefaultAsync(x => x.IdempotencyKey == key, ct);
            if (existing != null)
            {
                if (existing.CampaignId != campaignId) throw new ArgumentException("Idempotency key đã dùng cho nội dung khác.");
                return (await ToDtosAsync(new[] { existing }, ct))[0];
            }
        }
        EnsureOpen(c);
        var item = new GrowthScheduledContent
        {
            CampaignId = c.Id,
            Kind = GrowthKinds.All.Contains(input.Kind) ? input.Kind! : throw new ArgumentException("Loại nội dung không hợp lệ (Reel, Photo, Album, Story, Interactive)."),
            IdempotencyKey = key.Length > 0 ? key : Guid.NewGuid().ToString("N"),
            Status = GrowthStatus.Draft
        };
        await ApplyAsync(c, item, input, ct);
        if (input.Submit == true) await SubmitInternalAsync(c, item, ct);
        _db.GrowthScheduledContents.Add(item);
        Log(c.Id, item.Id, "Info", "ContentCreated", $"Thêm {KindLabel(item.Kind)} \"{Short(item.Title, item.Caption)}\" ({StatusLabel(item.Status)}).");
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (key.Length > 0)
        {
            // the same request arrived twice at the same moment: the first one won
            if (_db is DbContext ctx) ctx.ChangeTracker.Clear();
            var first = await _db.GrowthScheduledContents.AsNoTracking().FirstOrDefaultAsync(x => x.IdempotencyKey == key && x.CampaignId == campaignId, ct)
                        ?? throw new ArgumentException("Idempotency key đã dùng cho nội dung khác.");
            return (await ToDtosAsync(new[] { first }, ct))[0];
        }
        return await GetAsync(c.Id, item.Id, ct);
    }

    public async Task<GrowthContentDto> UpdateAsync(Guid campaignId, Guid contentId, GrowthContentInputDto input, CancellationToken ct)
    {
        var c = await _campaigns.LoadAsync(campaignId, ct);
        EnsureOpen(c);
        var item = await LoadAsync(campaignId, contentId, ct);
        if (item.Status is not (GrowthStatus.Draft or GrowthStatus.PendingApproval or GrowthStatus.Scheduled or GrowthStatus.Failed))
            throw new InvalidOperationException($"Nội dung đang ở trạng thái {StatusLabel(item.Status)} — không sửa được.");
        if (input.Kind != null && input.Kind != item.Kind) throw new ArgumentException("Không đổi loại nội dung — hãy tạo nội dung mới.");
        var before = (item.Caption, item.Hashtags, item.Title, item.SourceId, item.AssetIdsJson, item.ManualPublish);
        await ApplyAsync(c, item, input, ct);
        var contentChanged = before != (item.Caption, item.Hashtags, item.Title, item.SourceId, item.AssetIdsJson, item.ManualPublish);
        // an approved item whose content changed needs approval again (moving its time only does not)
        if (contentChanged && item.Status == GrowthStatus.Scheduled && c.ApprovalMode == "Review") item.Status = GrowthStatus.PendingApproval;
        if (item.Status == GrowthStatus.Scheduled) ValidateSchedulable(c, item, requireFuture: true);
        Log(c.Id, item.Id, "Info", "ContentUpdated", $"Sửa \"{Short(item.Title, item.Caption)}\"" + (item.ScheduledAtUtc is { } at ? $" — lịch {GrowthTime.ToLocal(at):dd/MM HH:mm}." : "."));
        await _db.SaveChangesAsync(ct);
        return await GetAsync(campaignId, contentId, ct);
    }

    private static void EnsureOpen(GrowthCampaign c)
    {
        if (c.Status is GrowthStatus.Completed or GrowthStatus.Cancelled)
            throw new InvalidOperationException("Chiến dịch đã kết thúc — không thêm hay sửa nội dung.");
    }

    [GeneratedRegex(@"#[\p{L}\p{N}_]+")]
    private static partial Regex HashtagPattern();

    public static int CountHashtags(string caption, string hashtags) =>
        HashtagPattern().Matches(caption + " " + hashtags).Select(m => m.Value.ToLowerInvariant()).Distinct().Count();

    private async Task ApplyAsync(GrowthCampaign c, GrowthScheduledContent item, GrowthContentInputDto input, CancellationToken ct)
    {
        if (input.Title != null) item.Title = GrowthCampaignService.Clean(input.Title, 300) ?? "";
        if (input.Caption != null)
        {
            var cap = input.Caption.Trim();
            if (cap.Length > MaxCaption) throw new ArgumentException($"Caption quá dài (tối đa {MaxCaption} ký tự).");
            item.Caption = cap;
        }
        if (input.Hashtags != null) item.Hashtags = GrowthCampaignService.Clean(input.Hashtags, 500) ?? "";
        if (CountHashtags(item.Caption, item.Hashtags) > MaxHashtags)
            throw new ArgumentException($"Tối đa {MaxHashtags} hashtag mỗi bài — nhiều hơn dễ bị coi là spam.");
        if (input.Style != null) item.Style = GrowthCampaignService.Clean(input.Style, 30);
        if (input.Cta != null) item.Cta = GrowthCampaignService.Clean(input.Cta, 500);
        if (input.Topic != null) item.Topic = GrowthCampaignService.Clean(input.Topic, 200);
        if (input.ManualPublish != null) item.ManualPublish = input.ManualPublish.Value;
        if (input.ScheduledAtUtc != null) item.ScheduledAtUtc = DateTime.SpecifyKind(input.ScheduledAtUtc.Value.ToUniversalTime(), DateTimeKind.Utc);

        if (input.SourceType != null || input.SourceId != null)
        {
            if (string.IsNullOrWhiteSpace(input.SourceType) || input.SourceId == null) { item.SourceType = null; item.SourceId = null; }
            else
            {
                if (!GrowthSources.All.Contains(input.SourceType)) throw new ArgumentException("Nguồn video không hợp lệ.");
                await EnsureSourceAsync(c, input.SourceType, input.SourceId.Value, item.Kind, ct);
                item.SourceType = input.SourceType;
                item.SourceId = input.SourceId;
            }
        }
        if (input.AssetIds != null)
        {
            var ids = input.AssetIds.Distinct().ToList();
            if (ids.Count > MaxAlbumImages) throw new ArgumentException($"Tối đa {MaxAlbumImages} tệp mỗi bài.");
            var owned = await _db.GrowthContentAssets.AsNoTracking().Where(a => ids.Contains(a.Id) && a.CampaignId == c.Id).Select(a => a.Id).ToListAsync(ct);
            if (owned.Count != ids.Count) throw new ArgumentException("Có tệp không thuộc chiến dịch này.");
            item.AssetIdsJson = JsonSerializer.Serialize(ids, Json);
        }
        await ValidateShapeAsync(item, ct);
    }

    /// <summary>What each kind needs (checked when saving, so problems show before the posting time).</summary>
    private async Task ValidateShapeAsync(GrowthScheduledContent item, CancellationToken ct)
    {
        var ids = AssetIds(item.AssetIdsJson);
        var assets = ids.Count == 0 ? new List<GrowthContentAsset>() : await _db.GrowthContentAssets.AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        var images = assets.Count(a => a.Kind == "Image");
        var videos = assets.Count(a => a.Kind == "Video");
        switch (item.Kind)
        {
            case GrowthKinds.Reel:
                if (assets.Count > 0) throw new ArgumentException("Reels lấy video từ kho video hiện có — không đính kèm tệp.");
                break;
            case GrowthKinds.Photo:
                if (item.SourceType != null || videos > 0 || images > 1) throw new ArgumentException("Bài ảnh đơn có đúng 1 ảnh.");
                break;
            case GrowthKinds.Album:
                if (item.SourceType != null || videos > 0) throw new ArgumentException("Bài nhiều ảnh chỉ gồm ảnh.");
                break;
            case GrowthKinds.Story:
                if (assets.Count + (item.SourceType != null ? 1 : 0) > 1) throw new ArgumentException("Mỗi Story là 1 ảnh hoặc 1 video.");
                if (item.SourceType == GrowthSources.VideoProject) throw new ArgumentException("Story chỉ dùng video từ Studio Video AI / AI Video Studio hoặc video tải lên.");
                if (assets.FirstOrDefault(a => a.Kind == "Video") is { DurationSeconds: > MaxStorySeconds } v)
                    throw new ArgumentException($"Video Story dài tối đa {MaxStorySeconds:0} giây (video này {v.DurationSeconds:0.#} giây).");
                break;
            case GrowthKinds.Interactive:
                if (item.SourceType != null || videos > 0) throw new ArgumentException("Bài tương tác là chữ, có thể kèm ảnh.");
                break;
        }
    }

    /// <summary>Everything needed to post (checked when the item enters the calendar).</summary>
    private static void ValidateSchedulable(GrowthCampaign c, GrowthScheduledContent item, bool requireFuture, int assetCount = -1)
    {
        if (item.ScheduledAtUtc is not { } at) throw new ArgumentException("Hãy chọn giờ đăng.");
        if (at < GrowthTime.DayStartUtc(c.StartDate) || at >= GrowthTime.EndUtc(c.EndDate))
            throw new ArgumentException($"Giờ đăng phải nằm trong chiến dịch ({c.StartDate:dd/MM} – {c.EndDate:dd/MM}).");
        if (requireFuture && at < DateTime.UtcNow.AddMinutes(-5)) throw new ArgumentException("Giờ đăng đã qua — chọn giờ khác hoặc bấm \"Đăng ngay\".");
        var assets = assetCount >= 0 ? assetCount : AssetIds(item.AssetIdsJson).Count;
        switch (item.Kind)
        {
            case GrowthKinds.Reel when item.SourceId == null:
                throw new ArgumentException("Hãy chọn video cho Reels.");
            case GrowthKinds.Photo when assets != 1:
                throw new ArgumentException("Bài ảnh đơn cần 1 ảnh.");
            case GrowthKinds.Album when assets < 2:
                throw new ArgumentException("Bài nhiều ảnh cần từ 2 ảnh.");
            case GrowthKinds.Story when assets + (item.SourceId != null ? 1 : 0) != 1:
                throw new ArgumentException("Story cần 1 ảnh hoặc 1 video.");
            case GrowthKinds.Interactive when string.IsNullOrWhiteSpace(item.Caption):
                throw new ArgumentException("Bài tương tác cần nội dung.");
        }
    }

    /// <summary>The video must be finished and belong to this Page (or to no Page). A Studio video already posted can't be posted again.</summary>
    private async Task EnsureSourceAsync(GrowthCampaign c, string type, Guid id, string kind, CancellationToken ct)
    {
        switch (type)
        {
            case GrowthSources.StudioVideo:
                var v = await _db.StudioVideos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ArgumentException("Không tìm thấy video Studio.");
                if (v.FacebookAccountId != null && v.FacebookAccountId != c.FacebookAccountId) throw new ArgumentException("Video này thuộc Page khác.");
                if (v.FinalBlobId == null) throw new ArgumentException("Video Studio chưa ghép xong.");
                // a Story may reuse a posted video (a highlight of it); a Reel may not be posted twice
                if (kind == GrowthKinds.Reel && (v.Status == "Published" || v.PublishedPostId != null)) throw new ArgumentException("Video này đã được đăng từ Studio — chọn video khác.");
                break;
            case GrowthSources.AiStudioProject:
                var p = await _db.AiStudioProjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ArgumentException("Không tìm thấy dự án AI Video Studio.");
                if (p.FacebookAccountId != null && p.FacebookAccountId != c.FacebookAccountId) throw new ArgumentException("Video này thuộc Page khác.");
                if (p.FinalStatus != "Done" || p.FinalBlobId == null) throw new ArgumentException("Video của dự án chưa ghép xong.");
                break;
            case GrowthSources.VideoProject:
                var n = await _db.VideoProjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ArgumentException("Không tìm thấy video tin tức.");
                if (n.FacebookAccountId != null && n.FacebookAccountId != c.FacebookAccountId) throw new ArgumentException("Video này thuộc Page khác.");
                if (n.Status == VideoProjectStatus.Published) throw new ArgumentException("Video tin tức này đã được đăng.");
                if (n.Status != VideoProjectStatus.AwaitingApproval) throw new ArgumentException("Video tin tức chưa dựng xong.");
                break;
        }
    }

    // ───────────────────────── approval flow ─────────────────────────

    private async Task SubmitInternalAsync(GrowthCampaign c, GrowthScheduledContent item, CancellationToken ct)
    {
        var assetCount = AssetIds(item.AssetIdsJson).Count;
        ValidateSchedulable(c, item, requireFuture: true, assetCount);
        if (item.Kind == GrowthKinds.Story) await EnsureStoryMediaAsync(item, ct);
        item.Status = c.ApprovalMode == "Auto" ? GrowthStatus.Scheduled : GrowthStatus.PendingApproval;
    }

    private async Task EnsureStoryMediaAsync(GrowthScheduledContent item, CancellationToken ct)
    {
        var ids = AssetIds(item.AssetIdsJson);
        if (ids.Count == 0) return;
        var a = await _db.GrowthContentAssets.AsNoTracking().FirstAsync(x => x.Id == ids[0], ct);
        if (a.Kind == "Video" && a.DurationSeconds > MaxStorySeconds) throw new ArgumentException($"Video Story dài tối đa {MaxStorySeconds:0} giây.");
    }

    public async Task<GrowthContentDto> SubmitAsync(Guid campaignId, Guid contentId, CancellationToken ct)
    {
        var c = await _campaigns.LoadAsync(campaignId, ct);
        EnsureOpen(c);
        var item = await LoadAsync(campaignId, contentId, ct);
        if (item.Status != GrowthStatus.Draft) throw new InvalidOperationException("Chỉ gửi lên lịch nội dung đang là bản nháp.");
        await SubmitInternalAsync(c, item, ct);
        Log(c.Id, item.Id, "Info", "ContentSubmitted", $"Đưa \"{Short(item.Title, item.Caption)}\" vào lịch ({StatusLabel(item.Status)}).");
        await _db.SaveChangesAsync(ct);
        return await GetAsync(campaignId, contentId, ct);
    }

    public async Task<GrowthContentDto> ApproveAsync(Guid campaignId, Guid contentId, CancellationToken ct)
    {
        var c = await _campaigns.LoadAsync(campaignId, ct);
        EnsureOpen(c);
        var item = await LoadAsync(campaignId, contentId, ct);
        if (item.Status is not (GrowthStatus.PendingApproval or GrowthStatus.Draft)) throw new InvalidOperationException("Nội dung không ở trạng thái chờ duyệt.");
        ValidateSchedulable(c, item, requireFuture: true);
        if (item.Kind == GrowthKinds.Story) await EnsureStoryMediaAsync(item, ct);
        item.Status = GrowthStatus.Scheduled;
        Log(c.Id, item.Id, "Info", "ContentApproved", $"Duyệt \"{Short(item.Title, item.Caption)}\" — đăng lúc {GrowthTime.ToLocal(item.ScheduledAtUtc!.Value):dd/MM HH:mm}.");
        await _db.SaveChangesAsync(ct);
        return await GetAsync(campaignId, contentId, ct);
    }

    public async Task<GrowthContentDto> CancelAsync(Guid campaignId, Guid contentId, CancellationToken ct)
    {
        await _campaigns.LoadAsync(campaignId, ct);
        var item = await LoadAsync(campaignId, contentId, ct);
        if (item.Status is GrowthStatus.Publishing or GrowthStatus.Published or GrowthStatus.Cancelled)
            throw new InvalidOperationException($"Không huỷ được nội dung đang ở trạng thái {StatusLabel(item.Status)}.");
        // atomic: the scheduler may be claiming it right now
        var n = await _db.GrowthScheduledContents.Where(x => x.Id == item.Id && x.Status == item.Status)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, GrowthStatus.Cancelled).SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), ct);
        if (n == 0) throw new InvalidOperationException("Nội dung vừa được bắt đầu đăng — không huỷ được nữa.");
        Log(campaignId, item.Id, "Info", "ContentCancelled", $"Huỷ \"{Short(item.Title, item.Caption)}\".");
        await _db.SaveChangesAsync(ct);
        return await GetAsync(campaignId, contentId, ct);
    }

    public async Task DeleteAsync(Guid campaignId, Guid contentId, CancellationToken ct)
    {
        await _campaigns.LoadAsync(campaignId, ct);
        var item = await LoadAsync(campaignId, contentId, ct);
        if (item.Status is not (GrowthStatus.Draft or GrowthStatus.Cancelled)) throw new InvalidOperationException("Chỉ xoá được bản nháp hoặc nội dung đã huỷ.");
        _db.GrowthScheduledContents.Remove(item);
        Log(campaignId, null, "Info", "ContentDeleted", $"Xoá \"{Short(item.Title, item.Caption)}\".");
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>"Đăng ngay": the owner's explicit go — scheduled for now and handed to the publisher at once.</summary>
    public async Task<GrowthContentDto> PublishNowAsync(Guid campaignId, Guid contentId, CancellationToken ct)
    {
        var c = await _campaigns.LoadAsync(campaignId, ct);
        if (c.Status != GrowthStatus.Running) throw new InvalidOperationException("Chỉ đăng khi chiến dịch đang chạy (bấm Bắt đầu / Tiếp tục).");
        var item = await LoadAsync(campaignId, contentId, ct);
        if (item.ExternalPostId != null || item.Status == GrowthStatus.Published) throw new InvalidOperationException("Nội dung đã được đăng.");
        if (item.Status is not (GrowthStatus.Draft or GrowthStatus.PendingApproval or GrowthStatus.Scheduled or GrowthStatus.Failed))
            throw new InvalidOperationException($"Không đăng được nội dung đang ở trạng thái {StatusLabel(item.Status)}.");
        if (item.ManualPublish) throw new InvalidOperationException("Nội dung này đăng thủ công — đăng trên Facebook rồi bấm \"Đã đăng thủ công\".");
        item.ScheduledAtUtc = DateTime.UtcNow;
        ValidateSchedulable(c, item, requireFuture: false);
        item.Status = GrowthStatus.Scheduled;
        item.NextAttemptAtUtc = null;
        if (item.Attempts >= item.MaxAttempts) item.Attempts = 0;
        Log(c.Id, item.Id, "Info", "PublishNow", $"Yêu cầu đăng ngay \"{Short(item.Title, item.Caption)}\".");
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<GrowthPublisher>(p => p.PublishOneAsync(item.Id, CancellationToken.None));
        return await GetAsync(campaignId, contentId, ct);
    }

    public async Task<GrowthContentDto> RetryAsync(Guid campaignId, Guid contentId, CancellationToken ct)
    {
        var c = await _campaigns.LoadAsync(campaignId, ct);
        EnsureOpen(c);
        var item = await LoadAsync(campaignId, contentId, ct);
        if (item.Status != GrowthStatus.Failed) throw new InvalidOperationException("Chỉ thử lại nội dung đăng thất bại.");
        if (item.ExternalPostId != null) throw new InvalidOperationException("Nội dung đã có bài trên Facebook — không đăng lại để tránh trùng.");
        if (item.ScheduledAtUtc == null || item.ScheduledAtUtc < DateTime.UtcNow) item.ScheduledAtUtc = DateTime.UtcNow;
        ValidateSchedulable(c, item, requireFuture: false);
        item.Status = GrowthStatus.Scheduled;
        item.Attempts = 0;
        item.NextAttemptAtUtc = null;
        Log(c.Id, item.Id, "Info", "ContentRetry", $"Thử đăng lại \"{Short(item.Title, item.Caption)}\".");
        await _db.SaveChangesAsync(ct);
        return await GetAsync(campaignId, contentId, ct);
    }

    /// <summary>Posted by hand on Facebook (Stories the API can't post for this Page): recorded as such, never counted as posted by the app.</summary>
    public async Task<GrowthContentDto> MarkManualAsync(Guid campaignId, Guid contentId, GrowthMarkManualDto input, CancellationToken ct)
    {
        await _campaigns.LoadAsync(campaignId, ct);
        var item = await LoadAsync(campaignId, contentId, ct);
        if (item.Status is GrowthStatus.Published or GrowthStatus.Publishing or GrowthStatus.Cancelled)
            throw new InvalidOperationException($"Nội dung đang ở trạng thái {StatusLabel(item.Status)}.");
        var url = (input.PermalinkUrl ?? "").Trim();
        if (url.Length > 0 && !IsFacebookUrl(url)) throw new ArgumentException("Liên kết phải là địa chỉ facebook.com / fb.watch.");
        item.Status = GrowthStatus.Published;
        item.PublishedManually = true;
        item.PublishedAtUtc = DateTime.UtcNow;
        item.PermalinkUrl = url.Length > 0 ? url : null;
        item.LastError = null;
        Log(campaignId, item.Id, "Info", "MarkedManual", $"Đánh dấu đã đăng thủ công \"{Short(item.Title, item.Caption)}\".");
        await _db.SaveChangesAsync(ct);
        return await GetAsync(campaignId, contentId, ct);
    }

    public static bool IsFacebookUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && (u.Host == "facebook.com" || u.Host.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase) || u.Host == "fb.watch" || u.Host == "fb.com");

    // ───────────────────────── sources ─────────────────────────

    public async Task<List<GrowthVideoSourceDto>> SourcesAsync(Guid campaignId, CancellationToken ct)
    {
        var c = await _campaigns.LoadAsync(campaignId, ct);
        var pid = c.FacebookAccountId;
        var scheduled = await _db.GrowthScheduledContents.AsNoTracking()
            .Where(x => x.SourceId != null && x.Status != GrowthStatus.Cancelled).Select(x => x.SourceId!.Value).ToListAsync(ct);
        var list = new List<GrowthVideoSourceDto>();

        var studio = await _db.StudioVideos.AsNoTracking()
            .Where(v => v.FinalBlobId != null && (v.FacebookAccountId == null || v.FacebookAccountId == pid))
            .OrderByDescending(v => v.CreatedAtUtc).Take(60)
            .Select(v => new { v.Id, v.Title, v.Caption, v.FinalDurationSeconds, v.CreatedAtUtc, v.FacebookAccountId, v.Status, v.PublishedPostId }).ToListAsync(ct);
        list.AddRange(studio.Select(v => new GrowthVideoSourceDto
        {
            Type = GrowthSources.StudioVideo, TypeLabel = GrowthSources.Label(GrowthSources.StudioVideo), Id = v.Id, Title = v.Title, Caption = v.Caption,
            DurationSeconds = v.FinalDurationSeconds, CreatedAtUtc = v.CreatedAtUtc, FacebookAccountId = v.FacebookAccountId,
            AlreadyPublished = v.Status == "Published" || v.PublishedPostId != null, PreviewUrl = GrowthSources.PreviewUrl(GrowthSources.StudioVideo, v.Id),
            Scheduled = scheduled.Contains(v.Id)
        }));

        var ai = await _db.AiStudioProjects.AsNoTracking()
            .Where(p => p.FinalStatus == "Done" && p.FinalBlobId != null && (p.FacebookAccountId == null || p.FacebookAccountId == pid))
            .OrderByDescending(p => p.CreatedAtUtc).Take(60)
            .Select(p => new { p.Id, p.Title, p.Caption, p.CreatedAtUtc, p.FacebookAccountId }).ToListAsync(ct);
        var aiIds = ai.Select(p => p.Id).ToList();
        var aiPublished = await _db.GrowthScheduledContents.AsNoTracking()
            .Where(x => x.SourceId != null && aiIds.Contains(x.SourceId.Value) && x.Status == GrowthStatus.Published).Select(x => x.SourceId!.Value).ToListAsync(ct);
        list.AddRange(ai.Select(p => new GrowthVideoSourceDto
        {
            Type = GrowthSources.AiStudioProject, TypeLabel = GrowthSources.Label(GrowthSources.AiStudioProject), Id = p.Id, Title = p.Title,
            Caption = string.IsNullOrWhiteSpace(p.Caption) ? null : p.Caption, CreatedAtUtc = p.CreatedAtUtc, FacebookAccountId = p.FacebookAccountId,
            AlreadyPublished = aiPublished.Contains(p.Id), PreviewUrl = GrowthSources.PreviewUrl(GrowthSources.AiStudioProject, p.Id), Scheduled = scheduled.Contains(p.Id)
        }));

        var news = await _db.VideoProjects.AsNoTracking()
            .Where(p => p.Status == VideoProjectStatus.AwaitingApproval && (p.FacebookAccountId == null || p.FacebookAccountId == pid))
            .OrderByDescending(p => p.CreatedAtUtc).Take(30)
            .Select(p => new { p.Id, p.Title, p.ActualDurationSeconds, p.CreatedAtUtc, p.FacebookAccountId }).ToListAsync(ct);
        list.AddRange(news.Select(p => new GrowthVideoSourceDto
        {
            Type = GrowthSources.VideoProject, TypeLabel = GrowthSources.Label(GrowthSources.VideoProject), Id = p.Id, Title = p.Title,
            DurationSeconds = p.ActualDurationSeconds, CreatedAtUtc = p.CreatedAtUtc, FacebookAccountId = p.FacebookAccountId, Scheduled = scheduled.Contains(p.Id)
        }));
        return list.OrderByDescending(s => s.CreatedAtUtc).ToList();
    }

    // ───────────────────────── assets ─────────────────────────

    /// <summary>Kind and MIME type from the file's first bytes (the name / declared type are not trusted).</summary>
    public static (string Kind, string Mime)? Sniff(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return ("Image", "image/jpeg");
        if (head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47 && head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A)
            return ("Image", "image/png");
        if (head.Length >= 12 && head[4] == (byte)'f' && head[5] == (byte)'t' && head[6] == (byte)'y' && head[7] == (byte)'p')
        {
            var brand = System.Text.Encoding.ASCII.GetString(head.Slice(8, 4));
            return ("Video", brand.StartsWith("qt") ? "video/quicktime" : "video/mp4");
        }
        return null;
    }

    /// <summary>Width × height of a JPEG / PNG without decoding it (0 × 0 when unreadable).</summary>
    public static (int W, int H) ImageSize(byte[] data)
    {
        if (data.Length >= 24 && data[0] == 0x89 && data[1] == 0x50)
            return ((data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19], (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23]);
        var i = 2;
        while (data.Length > 1 && data[0] == 0xFF && data[1] == 0xD8 && i + 9 < data.Length)
        {
            if (data[i] != 0xFF) { i++; continue; }
            var marker = data[i + 1];
            if (marker == 0xFF) { i++; continue; } // fill byte
            if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7) { i += 2; continue; }
            var len = (data[i + 2] << 8) | data[i + 3];
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
                return ((data[i + 7] << 8) | data[i + 8], (data[i + 5] << 8) | data[i + 6]);
            i += 2 + len;
        }
        return (0, 0);
    }

    public async Task<GrowthAssetDto> UploadAsync(Guid campaignId, string tempPath, string fileName, string source, CancellationToken ct)
    {
        var c = await _campaigns.LoadAsync(campaignId, ct);
        EnsureOpen(c);
        var info = new FileInfo(tempPath);
        if (info.Length == 0) throw new ArgumentException("Tệp rỗng.");
        var head = new byte[16];
        await using (var fs = File.OpenRead(tempPath)) _ = await fs.ReadAsync(head, ct);
        var sniff = Sniff(head) ?? throw new ArgumentException("Chỉ nhận ảnh JPEG / PNG hoặc video MP4 / MOV.");
        var asset = new GrowthContentAsset
        {
            CampaignId = c.Id, Kind = sniff.Kind, MimeType = sniff.Mime, SizeBytes = info.Length, FileId = Guid.NewGuid(),
            FileName = SafeName(fileName, sniff.Mime), Source = source is "Template" or "Ai" ? source : "Upload"
        };
        if (sniff.Kind == "Image")
        {
            if (info.Length > MaxImageBytes) throw new ArgumentException($"Ảnh tối đa {MaxImageBytes / 1048576} MB.");
            var bytes = await File.ReadAllBytesAsync(tempPath, ct);
            if (sniff.Mime == "image/png")
            {
                // Instagram only takes JPEG (and Facebook uploads are sent as JPEG): convert once here
                var jpg = tempPath + ".jpg";
                try
                {
                    await _growthMedia.ToJpegAsync(tempPath, jpg, ct);
                    bytes = await File.ReadAllBytesAsync(jpg, ct);
                    asset.MimeType = "image/jpeg";
                    asset.FileName = Path.ChangeExtension(asset.FileName, ".jpg");
                    asset.SizeBytes = bytes.Length;
                }
                catch (InvalidOperationException) { /* keep the PNG: Facebook still takes it */ }
                finally { try { File.Delete(jpg); } catch { /* best effort */ } }
            }
            (asset.Width, asset.Height) = ImageSize(bytes);
            if (asset.Width <= 0 || asset.Height <= 0) throw new ArgumentException("Không đọc được ảnh (tệp hỏng?).");
            if (asset.Width > 10000 || asset.Height > 10000) throw new ArgumentException("Ảnh quá lớn (tối đa 10000 px mỗi cạnh).");
            await _files.SaveAsync(asset.FileId, bytes, ct);
        }
        else
        {
            if (info.Length > MaxVideoBytes) throw new ArgumentException($"Video tối đa {MaxVideoBytes / 1048576} MB.");
            var probe = await _media.ProbeAsync(tempPath, ct);
            if (!probe.HasVideo || probe.Duration <= 0) throw new ArgumentException("Tệp không có hình video.");
            asset.Width = probe.Width;
            asset.Height = probe.Height;
            asset.DurationSeconds = Math.Round(probe.Duration, 1);
            await _files.SaveFileAsync(asset.FileId, tempPath, sniff.Mime, keepLocal: false, ct);
        }
        _db.GrowthContentAssets.Add(asset);
        Log(c.Id, null, "Info", "AssetUploaded", $"Tải lên {(asset.Kind == "Image" ? "ảnh" : "video")} {asset.FileName} ({asset.Width}×{asset.Height}).");
        await _db.SaveChangesAsync(ct);
        return AssetDto(asset);
    }

    public async Task<List<GrowthAssetDto>> AssetsAsync(Guid campaignId, CancellationToken ct)
    {
        await _campaigns.LoadAsync(campaignId, ct);
        var list = await _db.GrowthContentAssets.AsNoTracking().Where(a => a.CampaignId == campaignId).OrderByDescending(a => a.CreatedAtUtc).Take(300).ToListAsync(ct);
        return list.Select(AssetDto).ToList();
    }

    public async Task DeleteAssetAsync(Guid campaignId, Guid assetId, CancellationToken ct)
    {
        await _campaigns.LoadAsync(campaignId, ct);
        var a = await _db.GrowthContentAssets.FirstOrDefaultAsync(x => x.Id == assetId && x.CampaignId == campaignId, ct)
                ?? throw new KeyNotFoundException("Không tìm thấy tệp trong chiến dịch này.");
        var idText = assetId.ToString();
        var used = await _db.GrowthScheduledContents.AnyAsync(x => x.CampaignId == campaignId && x.AssetIdsJson.Contains(idText)
            && x.Status != GrowthStatus.Cancelled, ct);
        if (used) throw new InvalidOperationException("Tệp đang được dùng trong lịch đăng.");
        _db.GrowthContentAssets.Remove(a);
        await _db.SaveChangesAsync(ct);
        _files.Delete(a.FileId);
    }

    /// <summary>Local path + type of an asset's file, for serving / posting (null when gone).</summary>
    public async Task<(string Path, string Mime)?> AssetFileAsync(Guid assetId, CancellationToken ct)
    {
        var a = await _db.GrowthContentAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == assetId, ct);
        if (a == null) return null;
        var path = await _files.LocalPathAsync(a.FileId, ct);
        return path == null ? null : (path, a.MimeType);
    }

    /// <summary>
    /// A link Instagram can fetch the file from: a 1-hour link to the shared bucket, else the app's public address
    /// (App:PublicBaseUrl or the Railway domain). Null when the app has neither.
    /// </summary>
    public async Task<string?> PublicAssetUrlAsync(Guid assetId, CancellationToken ct)
    {
        var a = await _db.GrowthContentAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == assetId, ct);
        if (a == null) return null;
        var presigned = await _files.PresignedUrlAsync(a.FileId, a.MimeType, null, ct);
        if (presigned != null) return presigned;
        var configured = _configuration["App:PublicBaseUrl"];
        var domain = Environment.GetEnvironmentVariable("RAILWAY_PUBLIC_DOMAIN");
        var baseUrl = !string.IsNullOrWhiteSpace(configured) ? configured.TrimEnd('/')
            : !string.IsNullOrWhiteSpace(domain) ? $"https://{domain.Trim().TrimEnd('/')}" : null;
        return baseUrl == null ? null : $"{baseUrl}/api/growth/assets/{a.Id}/file";
    }

    /// <summary>
    /// Removes the file of an autopilot Story video once it is posted (storage is small; the Story lives on Facebook and
    /// Instagram). The asset row stays for the history.
    /// </summary>
    public async Task PurgePostedStoryVideosAsync(GrowthScheduledContent item, CancellationToken ct)
    {
        if (item.Kind != GrowthKinds.Story) return;
        var ids = AssetIds(item.AssetIdsJson);
        var videos = await _db.GrowthContentAssets.Where(a => ids.Contains(a.Id) && a.Kind == "Video" && a.Source == "Ai").ToListAsync(ct);
        foreach (var v in videos)
        {
            _files.Delete(v.FileId);
            v.SizeBytes = 0;
        }
        if (videos.Count > 0) await _db.SaveChangesAsync(ct);
    }

    private static string SafeName(string name, string mime)
    {
        var ext = mime switch { "image/png" => ".png", "image/jpeg" => ".jpg", "video/quicktime" => ".mov", _ => ".mp4" };
        var stem = Path.GetFileNameWithoutExtension(name ?? "");
        stem = new string(stem.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or ' ').ToArray()).Trim();
        if (stem.Length == 0) stem = "tep";
        if (stem.Length > 80) stem = stem[..80];
        return stem + ext;
    }

    // ───────────────────────── helpers ─────────────────────────

    public static string KindLabel(string kind) => kind switch
    {
        GrowthKinds.Reel => "Reels", GrowthKinds.Photo => "ảnh đơn", GrowthKinds.Album => "bài nhiều ảnh",
        GrowthKinds.Story => "Story", GrowthKinds.Interactive => "bài tương tác", _ => kind
    };

    public static string StatusLabel(string status) => status switch
    {
        GrowthStatus.Draft => "Nháp", GrowthStatus.PendingApproval => "Chờ duyệt", GrowthStatus.Scheduled => "Đã lên lịch",
        GrowthStatus.Publishing => "Đang đăng", GrowthStatus.Published => "Đã đăng", GrowthStatus.Failed => "Thất bại",
        GrowthStatus.Cancelled => "Đã huỷ", _ => status
    };

    public static string Short(string title, string caption)
    {
        var s = !string.IsNullOrWhiteSpace(title) ? title : caption;
        s = (s ?? "").Replace('\n', ' ').Trim();
        return s.Length > 60 ? s[..60] + "…" : s.Length == 0 ? "(không tiêu đề)" : s;
    }

    private void Log(Guid campaignId, Guid? contentId, string level, string action, string message, string actor = "user") =>
        _db.GrowthActivityLogs.Add(new GrowthActivityLog
        {
            CampaignId = campaignId, ContentId = contentId, Level = level, Action = action,
            Message = message.Length > 2000 ? message[..2000] : message, Actor = actor
        });
}
