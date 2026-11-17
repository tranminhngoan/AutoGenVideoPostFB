using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Domain.Enums;
using NewsStudio.Infrastructure.Common;

namespace NewsStudio.Infrastructure.Facebook;

public class FacebookPublishingService : IFacebookPublishingService
{
    private readonly IApplicationDbContext _context;
    private readonly IFacebookTokenProtectionService _tokenProtection;
    private readonly IVideoAssetService _assetService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<FacebookPublishingService> _logger;
    private readonly string _graphApiVersion;
    private readonly bool _simulate;

    public FacebookPublishingService(
        IApplicationDbContext context,
        IFacebookTokenProtectionService tokenProtection,
        IVideoAssetService assetService,
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<FacebookPublishingService> logger)
    {
        _context = context;
        _tokenProtection = tokenProtection;
        _assetService = assetService;
        _httpClient = httpClient;
        _logger = logger;
        _graphApiVersion = configuration["Facebook:GraphApiVersion"] ?? "v21.0";
        _simulate = SimulationMode.IsEnabled(configuration);
    }

    public async Task<VerifyTokenResultDto> VerifyTokenAsync(string pageAccessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageAccessToken))
        {
            return new VerifyTokenResultDto
            {
                IsValid = false,
                ErrorMessage = "Page Access Token không được để trống"
            };
        }

        string? graphError = null;
        try
        {
            var url = $"https://graph.facebook.com/{_graphApiVersion}/me?fields=id,name&access_token={pageAccessToken}";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                var pageId = doc.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                var pageName = doc.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;

                return new VerifyTokenResultDto
                {
                    IsValid = true,
                    PageId = pageId,
                    PageName = pageName,
                    Scopes = new List<string> { "pages_manage_posts", "pages_read_engagement", "publish_video" }
                };
            }

            graphError = await ReadGraphErrorAsync(response, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Network call to Meta Graph API failed during token verification: {Message}", ex.Message);
            graphError = "Không kết nối được tới Meta Graph API: " + ex.Message;
        }

        // Offline verification is only allowed in simulation mode (automated tests)
        if (_simulate && (pageAccessToken.StartsWith("EAAB") || pageAccessToken.StartsWith("test_token") || pageAccessToken.Length > 20))
        {
            return new VerifyTokenResultDto
            {
                IsValid = true,
                PageId = "100200300400500",
                PageName = "AI News Studio Official Page",
                Scopes = new List<string> { "pages_manage_posts", "pages_read_engagement", "publish_video" }
            };
        }

        return new VerifyTokenResultDto
        {
            IsValid = false,
            ErrorMessage = graphError ?? "Không thể xác thực Page Access Token với Meta Graph API. Vui lòng kiểm tra lại token và quyền truy cập."
        };
    }

    /// <summary>Extracts Meta's human-readable error message from a failed Graph API response.</summary>
    private static async Task<string?> ReadGraphErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            if (body.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
            {
                return $"Meta báo lỗi: {msg.GetString()}";
            }
            // rupload.facebook.com answers {"debug_info": {"type": ..., "message": ...}}
            if (body.TryGetProperty("debug_info", out var dbg) && dbg.TryGetProperty("message", out var dmsg))
            {
                return $"Meta báo lỗi: {dmsg.GetString()}";
            }
        }
        catch
        {
            // body was not JSON — fall through to the status code
        }
        return $"Meta Graph API trả về lỗi {(int)response.StatusCode} ({response.ReasonPhrase}).";
    }

    public async Task<List<FacebookAccountDto>> GetConnectedPagesAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await _context.FacebookAccounts
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return accounts.Select(a => new FacebookAccountDto
        {
            Id = a.Id,
            PageId = a.PageId,
            PageName = a.PageName,
            IsActive = a.IsActive,
            HasToken = !string.IsNullOrWhiteSpace(a.EncryptedAccessToken),
            MaskedToken = _tokenProtection.MaskToken(_tokenProtection.DecryptToken(a.EncryptedAccessToken)),
            TokenExpiresAtUtc = a.TokenExpiresAtUtc,
            Status = a.Status,
            CreatedAtUtc = a.CreatedAtUtc,
            InstagramUsername = a.InstagramUsername
        }).ToList();
    }

    public async Task<FacebookAccountDto> ConnectPageAsync(ConnectFacebookPageRequestDto request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting Facebook Page {PageId} ({PageName})", request.PageId, request.PageName);

        if (string.IsNullOrWhiteSpace(request.PageId) || string.IsNullOrWhiteSpace(request.PageAccessToken))
        {
            throw new ArgumentException("PageId và PageAccessToken không được để trống.");
        }

        // Check if page already connected
        var existing = await _context.FacebookAccounts
            .FirstOrDefaultAsync(a => a.PageId == request.PageId, cancellationToken);

        var encryptedToken = _tokenProtection.EncryptToken(request.PageAccessToken);

        if (existing != null)
        {
            existing.PageName = request.PageName;
            existing.EncryptedAccessToken = encryptedToken;
            existing.TokenExpiresAtUtc = request.TokenExpiresAtUtc;
            existing.IsActive = true;
            existing.Status = "Active";
            existing.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            return new FacebookAccountDto
            {
                Id = existing.Id,
                PageId = existing.PageId,
                PageName = existing.PageName,
                IsActive = existing.IsActive,
                HasToken = true,
                MaskedToken = _tokenProtection.MaskToken(request.PageAccessToken),
                TokenExpiresAtUtc = existing.TokenExpiresAtUtc,
                Status = existing.Status,
                CreatedAtUtc = existing.CreatedAtUtc
            };
        }

        var newAccount = new FacebookAccount
        {
            PageId = request.PageId,
            PageName = request.PageName,
            EncryptedAccessToken = encryptedToken,
            TokenExpiresAtUtc = request.TokenExpiresAtUtc,
            IsActive = true,
            Status = "Active"
        };
        _context.FacebookAccounts.Add(newAccount);
        await _context.SaveChangesAsync(cancellationToken);

        return new FacebookAccountDto
        {
            Id = newAccount.Id,
            PageId = newAccount.PageId,
            PageName = newAccount.PageName,
            IsActive = newAccount.IsActive,
            HasToken = true,
            MaskedToken = _tokenProtection.MaskToken(request.PageAccessToken),
            TokenExpiresAtUtc = newAccount.TokenExpiresAtUtc,
            Status = newAccount.Status,
            CreatedAtUtc = newAccount.CreatedAtUtc
        };
    }

    public async Task<bool> DisconnectPageAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await _context.FacebookAccounts.FindAsync(new object[] { accountId }, cancellationToken);
        if (account == null) return false;

        account.IsActive = false;
        account.Status = "Disconnected";
        account.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PublishReelResultDto> PublishReelAsync(Guid videoProjectId, PublishReelRequestDto? request = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Beginning Facebook Reels publishing pipeline for VideoProject {ProjectId}", videoProjectId);

        var project = await _context.VideoProjects
            .Include(p => p.Script)
            .Include(p => p.Reviews)
            .Include(p => p.Assets)
            .FirstOrDefaultAsync(p => p.Id == videoProjectId, cancellationToken);

        if (project == null)
        {
            throw new ArgumentException($"Video project with ID {videoProjectId} not found");
        }

        // 1. EDITORIAL APPROVAL CHECK (MANDATORY REQUIREMENT)
        var latestReview = project.Reviews.OrderByDescending(r => r.CreatedAtUtc).FirstOrDefault();
        if (latestReview == null || !latestReview.Decision.Equals("Approved", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Video chưa được ban biên tập phê duyệt! Bắt buộc phải duyệt video trước khi đăng lên Facebook Reels.");
        }

        if (latestReview.SeriousViolationsFound)
        {
            throw new InvalidOperationException("Không thể xuất bản vì video còn tồn tại lỗi nghiêm trọng chặn xuất bản!");
        }

        // 2. IDEMPOTENCY CHECK (PREVENT DUPLICATE REELS PUBLISHING)
        var existingPublishedPost = await _context.FacebookPosts
            .FirstOrDefaultAsync(p => p.VideoProjectId == videoProjectId && p.UploadStatus == "Published", cancellationToken);

        if (existingPublishedPost != null)
        {
            _logger.LogWarning("Project {ProjectId} has already been published to Facebook Reels (PostId: {PostId})",
                videoProjectId, existingPublishedPost.FacebookPostId);
            return new PublishReelResultDto
            {
                FacebookPostRecordId = existingPublishedPost.Id,
                FacebookPostId = existingPublishedPost.FacebookPostId ?? string.Empty,
                FacebookVideoId = existingPublishedPost.FacebookVideoId ?? string.Empty,
                PostUrl = $"https://www.facebook.com/reel/{existingPublishedPost.FacebookVideoId}",
                UploadStatus = "Published",
                PublishedAtUtc = existingPublishedPost.PublishedAtUtc,
                EphemeralAssetsCleanedUp = false
            };
        }

        // 3. SELECT CONNECTED PAGE & RESOLVE TOKEN
        FacebookAccount? account = null;
        if (request?.FacebookAccountId.HasValue == true)
        {
            account = await _context.FacebookAccounts.FindAsync(new object[] { request.FacebookAccountId.Value }, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request?.PageId))
        {
            account = await _context.FacebookAccounts.FirstOrDefaultAsync(a => a.PageId == request.PageId && a.IsActive, cancellationToken);
        }
        else if (project.FacebookAccountId != null
                 && await _context.FacebookAccounts.AnyAsync(a => a.Id == project.FacebookAccountId && a.IsActive, cancellationToken))
        {
            // The page the video was made for (its name is on the video)
            account = await _context.FacebookAccounts.FindAsync(new object[] { project.FacebookAccountId.Value }, cancellationToken);
        }
        else
        {
            account = await _context.FacebookAccounts.FirstOrDefaultAsync(a => a.IsActive, cancellationToken);
        }

        if (account == null && !_simulate)
        {
            throw new InvalidOperationException(
                "Chưa kết nối trang Facebook nào. Vào Cài đặt → Facebook để kết nối trang trước khi đăng.");
        }

        if (account == null)
        {
            // Simulation mode only: auto-create a demo page so offline tests can exercise the flow
            account = new FacebookAccount
            {
                PageId = "100200300400500",
                PageName = "AI News Studio Fanpage",
                EncryptedAccessToken = _tokenProtection.EncryptToken("EAAB_test_mock_token_secure_access_token_12345"),
                IsActive = true,
                Status = "Active"
            };
            _context.FacebookAccounts.Add(account);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var rawPageToken = _tokenProtection.DecryptToken(account.EncryptedAccessToken);

        // 4. LOCATE EPHEMERAL FINAL VIDEO ASSET
        var videoAsset = project.Assets.FirstOrDefault(a => (a.AssetType == "FinalVideo" || a.AssetType == "Video") && !a.IsCleanedUp);
        var videoFilePath = videoAsset?.LocalTempPath ?? project.OutputVideoPath;

        if ((string.IsNullOrWhiteSpace(videoFilePath) || !File.Exists(videoFilePath)) && !_simulate)
        {
            throw new InvalidOperationException(
                "Không tìm thấy file video trên máy chủ (có thể đã bị xoá do hết hạn). Hãy dựng lại video rồi đăng.");
        }

        if (string.IsNullOrWhiteSpace(videoFilePath) || !File.Exists(videoFilePath))
        {
            // Simulation mode only: create a tiny placeholder MP4 container for the offline test pipeline
            var tempDir = Path.Combine(Path.GetTempPath(), "newsstudio_reels");
            Directory.CreateDirectory(tempDir);
            videoFilePath = Path.Combine(tempDir, $"reel_upload_{videoProjectId}.mp4");
            if (!File.Exists(videoFilePath))
            {
                await File.WriteAllBytesAsync(videoFilePath, new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D }, cancellationToken);
            }
        }

        // 5. BUILD REELS CAPTION / DESCRIPTION
        var caption = request?.CustomCaption;
        if (string.IsNullOrWhiteSpace(caption))
        {
            var title = project.Title;
            var hook = project.Script?.Hook ?? string.Empty;
            var source = project.Script?.SourceAttribution ?? string.Empty;
            caption = $"{title}\n\n{hook}\n\n{source}\n#TinTuc #Reels";
        }

        // Update Project Status to Publishing
        project.Status = VideoProjectStatus.Publishing;
        project.UpdatedAtUtc = DateTime.UtcNow;

        var postRecord = new FacebookPost
        {
            VideoProjectId = videoProjectId,
            FacebookAccountId = account.Id,
            UploadStatus = "SessionCreated",
            Status = "Publishing"
        };
        _context.FacebookPosts.Add(postRecord);
        await _context.SaveChangesAsync(cancellationToken);

        string fbVideoId;
        string fbPostId;

        try
        {
            // Try Real Meta Graph API call
            var (realVideoId, realPostId) = await ExecuteMetaReelsPublishAsync(
                account.PageId,
                rawPageToken,
                videoFilePath,
                caption,
                cancellationToken);

            fbVideoId = realVideoId;
            fbPostId = realPostId;
        }
        catch (Exception ex) when (!_simulate)
        {
            // Real failure: record it honestly and send the project back to the approval queue.
            _logger.LogError(ex, "Meta Reels upload failed for project {ProjectId}", videoProjectId);
            postRecord.UploadStatus = "Failed";
            postRecord.Status = "Failed";
            postRecord.ErrorMessage = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            postRecord.UpdatedAtUtc = DateTime.UtcNow;
            project.Status = VideoProjectStatus.AwaitingApproval;
            project.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(CancellationToken.None);
            throw new InvalidOperationException($"Đăng lên Facebook thất bại: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Graph API upload failed ({Message}). Simulation mode: recording simulated publication IDs.", ex.Message);
            // Simulation mode only: deterministic IDs for offline tests (kept short to fit within DB column length)
            var shortPage = account.PageId.Length > 20 ? account.PageId.Substring(0, 20) : account.PageId;
            var numSuffix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            fbVideoId = $"vid_{numSuffix}";
            fbPostId = $"{shortPage}_{fbVideoId}";
        }

        // 6. RECORD SUCCESSFUL PUBLICATION IN POSTGRESQL
        postRecord.FacebookPostId = fbPostId;
        postRecord.FacebookVideoId = fbVideoId;
        postRecord.UploadStatus = "Published";
        postRecord.PublishedAtUtc = DateTime.UtcNow;
        postRecord.Status = "Published";
        postRecord.UpdatedAtUtc = DateTime.UtcNow;

        project.Status = VideoProjectStatus.Published;
        project.UpdatedAtUtc = DateTime.UtcNow;

        // Initialize first VideoMetric record (All zeros, strictly no fabricated numbers)
        var initialMetric = new VideoMetric
        {
            FacebookPostId = postRecord.Id,
            Views = 0,
            Reach = 0,
            Engagement = 0,
            Shares = 0,
            Comments = 0,
            WatchTimeSeconds = 0,
            CollectedAtUtc = DateTime.UtcNow,
            Status = "Initialized"
        };
        _context.VideoMetrics.Add(initialMetric);

        await _context.SaveChangesAsync(cancellationToken);

        // 6b. The page's own first comment (question + article link). The Reel is already live, so a
        //     failure is only recorded (e.g. the token lacks pages_manage_engagement).
        if (!string.IsNullOrWhiteSpace(request?.FirstComment) && !_simulate)
        {
            try
            {
                postRecord.FirstCommentId = await PostCommentAsync(rawPageToken, fbVideoId, fbPostId, request.FirstComment.Trim(), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("First comment on {PostId} failed: {Message}", fbPostId, ex.Message);
                postRecord.FirstCommentError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            }
            await _context.SaveChangesAsync(CancellationToken.None);
        }

        // 6c. The same Reel on the Instagram account linked to the page (the Facebook Reel is already live:
        //     a failure is only recorded).
        if (request?.PostToInstagram == true && !_simulate)
        {
            try
            {
                var ig = await ResolveInstagramAsync(account, rawPageToken, cancellationToken)
                         ?? throw new InvalidOperationException("Page chưa liên kết tài khoản Instagram chuyên nghiệp (Cài đặt Page → Tài khoản được liên kết → Instagram).");
                (string MediaId, string? Permalink) published;
                try
                {
                    published = await PublishInstagramReelAsync(ig, rawPageToken, videoFilePath, caption, cancellationToken);
                }
                catch (InvalidOperationException first) when (!first.Message.Contains("permission", StringComparison.OrdinalIgnoreCase))
                {
                    // Uploads / processing sometimes fail once on Meta's side: one fresh attempt
                    _logger.LogInformation("Instagram attempt 1 failed ({Message}); retrying once", first.Message);
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    published = await PublishInstagramReelAsync(ig, rawPageToken, videoFilePath, caption, cancellationToken);
                }
                var (mediaId, permalink) = published;
                postRecord.InstagramMediaId = mediaId;
                postRecord.InstagramPermalink = permalink;
                _logger.LogInformation("Reel of project {ProjectId} also posted to Instagram: {Permalink}", videoProjectId, permalink);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Instagram post for project {ProjectId} failed: {Message}", videoProjectId, ex.Message);
                postRecord.InstagramError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            }
            await _context.SaveChangesAsync(CancellationToken.None);
        }

        // 7. CLEAN UP EPHEMERAL ASSETS (STRICT MASTER PLAN REQUIREMENT)
        var cleanedUp = false;
        if (request?.AutoCleanupAssets != false)
        {
            try
            {
                _logger.LogInformation("Post-publish cleanup: purging ephemeral assets for project {ProjectId}", videoProjectId);
                await _assetService.CleanupProjectAssetsAsync(videoProjectId, cancellationToken);
                cleanedUp = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cleanup assets after publish: {Message}", ex.Message);
            }
        }

        return new PublishReelResultDto
        {
            FacebookPostRecordId = postRecord.Id,
            FacebookPostId = fbPostId,
            FacebookVideoId = fbVideoId,
            PostUrl = $"https://www.facebook.com/reel/{fbVideoId}",
            UploadStatus = "Published",
            PublishedAtUtc = postRecord.PublishedAtUtc,
            EphemeralAssetsCleanedUp = cleanedUp,
            FirstCommentId = postRecord.FirstCommentId,
            FirstCommentError = postRecord.FirstCommentError,
            InstagramMediaId = postRecord.InstagramMediaId,
            InstagramPermalink = postRecord.InstagramPermalink,
            InstagramError = postRecord.InstagramError
        };
    }

    // ---------------------------------------------------------------- Instagram

    /// <summary>
    /// The Instagram professional account linked to the page (id), refreshed from Meta and remembered on the
    /// account; null when the page has none.
    /// </summary>
    private async Task<string?> ResolveInstagramAsync(FacebookAccount account, string pageToken, CancellationToken ct)
    {
        var ig = await ReadInstagramAsync(account.PageId, pageToken, ct);
        if (ig != null && (account.InstagramAccountId != ig.Value.Id || account.InstagramUsername != ig.Value.Username))
        {
            account.InstagramAccountId = ig.Value.Id;
            account.InstagramUsername = ig.Value.Username;
            await _context.SaveChangesAsync(ct);
        }
        return ig?.Id;
    }

    private async Task<(string Id, string? Username)?> ReadInstagramAsync(string pageId, string pageToken, CancellationToken ct)
    {
        var (ok, body, error) = await GetGraphAsync(
            $"https://graph.facebook.com/{_graphApiVersion}/{pageId}?fields=instagram_business_account%7Bid,username%7D&access_token={Uri.EscapeDataString(pageToken)}", ct);
        if (!ok) throw new InvalidOperationException($"Không đọc được Instagram của Page. {error}");
        if (!body.TryGetProperty("instagram_business_account", out var ig) || !ig.TryGetProperty("id", out var id)) return null;
        return (id.GetString()!, ig.TryGetProperty("username", out var u) ? u.GetString() : null);
    }

    public async Task FetchInstagramMetricsAsync(Guid facebookPostId, CancellationToken cancellationToken = default)
    {
        var post = await _context.FacebookPosts
            .Include(p => p.FacebookAccount)
            .Include(p => p.InstagramMetrics)
            .FirstOrDefaultAsync(p => p.Id == facebookPostId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy bài đăng.");
        if (string.IsNullOrWhiteSpace(post.InstagramMediaId) || post.FacebookAccount == null)
            throw new InvalidOperationException("Bài này chưa được đăng lên Instagram.");

        var token = _tokenProtection.DecryptToken(post.FacebookAccount.EncryptedAccessToken);
        var baseUrl = $"https://graph.facebook.com/{_graphApiVersion}/{post.InstagramMediaId}";
        var (ok, body, error) = await GetGraphAsync($"{baseUrl}/insights?metric=views,reach,likes,comments,shares,saved&access_token={Uri.EscapeDataString(token)}", cancellationToken);
        if (!ok)
        {
            var permission = error != null && (error.Contains("permission", StringComparison.OrdinalIgnoreCase) || error.Contains("(#10)") || error.Contains("(#200)"));
            throw new InvalidOperationException(permission
                ? $"Token của Page chưa có quyền xem thống kê Instagram (instagram_manage_insights). Làm lại Kết nối nhanh, nhớ tick quyền này. {error}"
                : $"Không lấy được số liệu Instagram. {error}");
        }
        var values = InstagramInsightValues(body);

        // One snapshot per (Vietnam) day; lifetime counters only go up
        var now = DateTime.UtcNow;
        var dayStart = VietnamDayStartUtc(now);
        var metric = post.InstagramMetrics.Where(m => m.CollectedAtUtc >= dayStart).OrderByDescending(m => m.CollectedAtUtc).FirstOrDefault();
        if (metric == null)
        {
            metric = new InstagramMetric { FacebookPostId = post.Id };
            _context.InstagramMetrics.Add(metric);
        }
        var previous = post.InstagramMetrics.Where(m => m != metric).OrderByDescending(m => m.CollectedAtUtc).FirstOrDefault();
        long Up(string key, long? before) => Math.Max(values.GetValueOrDefault(key), before ?? 0);
        metric.Views = Up("views", previous?.Views);
        metric.Reach = Up("reach", previous?.Reach);
        metric.Likes = Up("likes", previous?.Likes);
        metric.Comments = Up("comments", previous?.Comments);
        metric.Shares = Up("shares", previous?.Shares);
        metric.Saved = Up("saved", previous?.Saved);
        metric.CollectedAtUtc = now;
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Instagram insights → metric name → number. Handles both shapes Meta uses:
    /// <c>values[0].value</c> and <c>total_value.value</c>.
    /// </summary>
    public static Dictionary<string, long> InstagramInsightValues(JsonElement body)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        if (!body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var n) || n.GetString() is not { } name) continue;
            JsonElement v = default;
            var found = item.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Array && vals.GetArrayLength() > 0
                        && vals[0].TryGetProperty("value", out v)
                        || item.TryGetProperty("total_value", out var tv) && tv.TryGetProperty("value", out v);
            if (found && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var number)) result[name] = number;
        }
        return result;
    }

    public async Task<List<FacebookAccountDto>> RefreshInstagramAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await _context.FacebookAccounts.Where(a => a.IsActive).ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            try
            {
                var ig = await ReadInstagramAsync(account.PageId, _tokenProtection.DecryptToken(account.EncryptedAccessToken), cancellationToken);
                account.InstagramAccountId = ig?.Id;
                account.InstagramUsername = ig?.Username;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Token can't read it (expired / no instagram_basic / unreadable): keep what we knew
                _logger.LogInformation("Instagram of page {Page} not readable: {Message}", account.PageName, ex.Message);
            }
        }
        await _context.SaveChangesAsync(cancellationToken);
        return await GetConnectedPagesAsync(cancellationToken);
    }

    /// <summary>Instagram limits captions to 2,200 characters.</summary>
    public static string InstagramCaption(string caption) =>
        caption.Length <= 2200 ? caption : caption[..2199] + "…";

    /// <summary>
    /// Posts a local MP4 as an Instagram Reel: resumable upload container → binary upload → wait until
    /// Instagram has processed it → publish. Returns the media id and its permalink.
    /// </summary>
    private async Task<(string MediaId, string? Permalink)> PublishInstagramReelAsync(string igUserId, string token, string videoPath, string caption, CancellationToken ct)
    {
        var baseUrl = $"https://graph.facebook.com/{_graphApiVersion}";

        // 1. Container
        using var create = await _httpClient.PostAsync($"{baseUrl}/{igUserId}/media", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["media_type"] = "REELS",
            ["upload_type"] = "resumable",
            ["caption"] = InstagramCaption(caption),
            ["share_to_feed"] = "true",
            ["access_token"] = token
        }), ct);
        if (!create.IsSuccessStatusCode) throw new InvalidOperationException($"Instagram không nhận video. {await ReadGraphErrorAsync(create, ct)}");
        var created = await create.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var containerId = created.GetProperty("id").GetString()!;
        var uploadUri = created.TryGetProperty("uri", out var u) ? u.GetString() : null;
        if (string.IsNullOrWhiteSpace(uploadUri)) throw new InvalidOperationException("Instagram không trả về địa chỉ tải video lên.");

        // 2. Binary upload (same rupload protocol as Facebook Reels)
        var size = new FileInfo(videoPath).Length;
        await using (var stream = File.OpenRead(videoPath))
        using (var upload = new HttpRequestMessage(HttpMethod.Post, uploadUri))
        {
            upload.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
            upload.Headers.TryAddWithoutValidation("offset", "0");
            upload.Headers.TryAddWithoutValidation("file_size", size.ToString());
            upload.Content = new StreamContent(stream);
            upload.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var res = await _httpClient.SendAsync(upload, ct);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Tải video lên Instagram thất bại. {await ReadGraphErrorAsync(res, ct)}");
        }

        // 3. Wait for Instagram to process the video (usually under a minute)
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(6), ct);
            var (ok, body, error) = await GetGraphAsync($"{baseUrl}/{containerId}?fields=status_code,status&access_token={Uri.EscapeDataString(token)}", ct);
            var status = ok && body.TryGetProperty("status_code", out var sc) ? sc.GetString() : null;
            if (status == "FINISHED") break;
            if (status is "ERROR" or "EXPIRED")
                throw new InvalidOperationException($"Instagram xử lý video lỗi: {(body.TryGetProperty("status", out var st) ? st.GetString() : status)}");
            if (DateTime.UtcNow > deadline)
                throw new InvalidOperationException($"Instagram xử lý video quá lâu (trạng thái: {status ?? error ?? "không rõ"}).");
        }

        // 4. Publish
        using var publish = await _httpClient.PostAsync($"{baseUrl}/{igUserId}/media_publish", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["creation_id"] = containerId,
            ["access_token"] = token
        }), ct);
        if (!publish.IsSuccessStatusCode) throw new InvalidOperationException($"Đăng Reels lên Instagram thất bại. {await ReadGraphErrorAsync(publish, ct)}");
        var published = await publish.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var mediaId = published.GetProperty("id").GetString()!;

        var (linkOk, linkBody, _) = await GetGraphAsync($"{baseUrl}/{mediaId}?fields=permalink&access_token={Uri.EscapeDataString(token)}", ct);
        var permalink = linkOk && linkBody.TryGetProperty("permalink", out var pl) ? pl.GetString() : null;
        return (mediaId, permalink);
    }

    /// <summary>Comments as the page under a Reel (on the video, or on its post when the video refuses).</summary>
    private async Task<string> PostCommentAsync(string token, string videoId, string postId, string message, CancellationToken ct)
    {
        string? lastError = null;
        foreach (var target in new[] { videoId, postId }.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct())
        {
            using var res = await _httpClient.PostAsync($"https://graph.facebook.com/{_graphApiVersion}/{target}/comments",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["message"] = message, ["access_token"] = token }), ct);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                return body.TryGetProperty("id", out var id) ? id.GetString() ?? target : target;
            }
            lastError = await ReadGraphErrorAsync(res, ct);
        }
        var permission = lastError != null && (lastError.Contains("permission", StringComparison.OrdinalIgnoreCase) || lastError.Contains("(#200)") || lastError.Contains("(#10)"));
        throw new InvalidOperationException(permission
            ? $"Token của Page chưa có quyền bình luận (pages_manage_engagement). Kết nối lại Page bằng Kết nối nhanh, nhớ tick quyền này. {lastError}"
            : $"Không bình luận được. {lastError}");
    }

    private async Task<(string VideoId, string PostId)> ExecuteMetaReelsPublishAsync(
        string pageId,
        string pageToken,
        string videoFilePath,
        string caption,
        CancellationToken cancellationToken)
    {
        // Step 1: Start upload session
        var startUrl = $"https://graph.facebook.com/{_graphApiVersion}/{pageId}/video_reels";
        var startPayload = new
        {
            upload_phase = "start",
            access_token = pageToken
        };
        var startRes = await _httpClient.PostAsJsonAsync(startUrl, startPayload, cancellationToken);
        if (!startRes.IsSuccessStatusCode)
        {
            var errBody = await startRes.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Meta Graph API error on video_reels start: {StatusCode} - {Body}", startRes.StatusCode, errBody);
            throw new InvalidOperationException($"Lỗi Meta Graph API (Khởi tạo upload): {errBody}");
        }

        var startData = await startRes.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var videoId = startData.GetProperty("video_id").GetString()!;
        var uploadUrl = startData.GetProperty("upload_url").GetString()!;

        // Step 2: Transfer binary video (rupload.facebook.com requires Offset and file_size headers)
        var fileInfo = new FileInfo(videoFilePath);
        using var fileStream = File.OpenRead(videoFilePath);
        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", pageToken);
        request.Headers.TryAddWithoutValidation("Offset", "0");
        request.Headers.TryAddWithoutValidation("file_size", fileInfo.Length.ToString());
        request.Content = new StreamContent(fileStream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var transferRes = await _httpClient.SendAsync(request, cancellationToken);
        if (!transferRes.IsSuccessStatusCode)
        {
            var errBody = await transferRes.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Meta Graph API error on video transfer: {StatusCode} - {Body}", transferRes.StatusCode, errBody);
            throw new InvalidOperationException($"Lỗi Meta Graph API (Tải file video lên): {errBody}");
        }

        // Step 3: Finish and Publish
        var finishPayload = new
        {
            upload_phase = "finish",
            access_token = pageToken,
            video_id = videoId,
            video_state = "PUBLISHED",
            description = caption
        };
        var finishRes = await _httpClient.PostAsJsonAsync(startUrl, finishPayload, cancellationToken);
        if (!finishRes.IsSuccessStatusCode)
        {
            var errBody = await finishRes.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Meta Graph API error on video_reels finish: {StatusCode} - {Body}", finishRes.StatusCode, errBody);
            throw new InvalidOperationException($"Lỗi Meta Graph API (Xuất bản Reels): {errBody}");
        }

        var postId = $"{pageId}_{videoId}";
        return (videoId, postId);
    }

    /// <summary>Permissions the app uses: listing pages, posting Reels/Stories/photos, reading views.</summary>
    public static readonly string[] RequiredPermissions =
        { "pages_show_list", "pages_read_engagement", "pages_manage_posts", "pages_manage_engagement", "read_insights",
          "instagram_basic", "instagram_content_publish", "instagram_manage_insights" };

    public async Task<UserTokenPagesResultDto> ConnectFromUserTokenAsync(UserTokenConnectRequestDto request, CancellationToken cancellationToken = default)
    {
        var userToken = (request.UserAccessToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(userToken)) throw new ArgumentException("Hãy dán mã truy cập người dùng.");
        var baseUrl = $"https://graph.facebook.com/{_graphApiVersion}";
        var result = new UserTokenPagesResultDto();

        // 1. The user token itself: extended or not, which permissions
        var (userExpires, scopes, userError) = await DebugTokenAsync(userToken, userToken, cancellationToken);
        if (userError != null) throw new InvalidOperationException($"Mã không dùng được. {userError}");
        result.UserTokenExpiresAtUtc = userExpires;
        result.LongLived = userExpires == null || userExpires > DateTime.UtcNow.AddDays(7);
        result.MissingPermissions = RequiredPermissions.Where(p => !scopes.Contains(p)).ToList();

        // 2. Every page the user manages, each with its own page token
        var pages = new List<(string Id, string Name, string Token)>();
        string? next = $"{baseUrl}/me/accounts?fields=id,name,access_token&limit=100&access_token={Uri.EscapeDataString(userToken)}";
        while (next != null && pages.Count < 500)
        {
            var (ok, body, error) = await GetGraphAsync(next, cancellationToken);
            if (!ok)
            {
                if (error != null && (error.Contains("node type (Page)") || error.Contains("nonexisting field (accounts)")))
                    throw new InvalidOperationException(
                        "Đây là mã của một Page, không phải mã người dùng. Trong Graph API Explorer, ô \"Người dùng hoặc Trang\" hãy chọn \"Quay lại mã người dùng\" rồi bấm Generate Access Token.");
                throw new InvalidOperationException($"Không lấy được danh sách Page. {error}");
            }
            if (body.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in data.EnumerateArray())
                {
                    var id = p.TryGetProperty("id", out var i) ? i.GetString() : null;
                    var token = p.TryGetProperty("access_token", out var t) ? t.GetString() : null;
                    if (id != null && token != null)
                        pages.Add((id, p.TryGetProperty("name", out var n) ? n.GetString() ?? id : id, token));
                }
            }
            next = body.TryGetProperty("paging", out var paging) && paging.TryGetProperty("next", out var nx) ? nx.GetString() : null;
        }
        if (pages.Count == 0)
            throw new InvalidOperationException("Mã này không quản lý Page nào. Khi bấm Generate Access Token, hãy tick chọn các Page trong cửa sổ Facebook.");

        // 3. Expiry of each page token, connect the chosen ones
        var connectedIds = (await _context.FacebookAccounts.AsNoTracking().Where(a => a.IsActive).Select(a => a.PageId).ToListAsync(cancellationToken))
            .ToHashSet();
        var wanted = (request.PageIds ?? new List<string>()).ToHashSet();
        foreach (var (id, name, token) in pages)
        {
            var (expires, _, _) = await DebugTokenAsync(token, userToken, cancellationToken);
            (string Id, string? Username)? instagram = null;
            try
            {
                instagram = await ReadInstagramAsync(id, token, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogInformation("Quick connect: Instagram of page {Page} not readable: {Message}", name, ex.Message);
            }
            var item = new UserTokenPageDto
            {
                PageId = id,
                PageName = name,
                AlreadyConnected = connectedIds.Contains(id),
                NeverExpires = expires == null,
                ExpiresAtUtc = expires,
                InstagramUsername = instagram == null ? null : instagram.Value.Username ?? instagram.Value.Id
            };
            if (wanted.Contains(id))
            {
                await ConnectPageAsync(new ConnectFacebookPageRequestDto
                {
                    PageId = id,
                    PageName = name,
                    PageAccessToken = token,
                    TokenExpiresAtUtc = expires
                }, cancellationToken);
                var saved = await _context.FacebookAccounts.FirstOrDefaultAsync(a => a.PageId == id, cancellationToken);
                if (saved != null)
                {
                    saved.InstagramAccountId = instagram?.Id;
                    saved.InstagramUsername = instagram?.Username;
                    await _context.SaveChangesAsync(cancellationToken);
                }
                item.Connected = true;
                item.AlreadyConnected = true;
            }
            result.Pages.Add(item);
        }
        _logger.LogInformation("Quick connect: {Count} pages listed, {Connected} connected (user token long-lived: {LongLived})",
            result.Pages.Count, result.Pages.Count(p => p.Connected), result.LongLived);
        return result;
    }

    /// <summary>debug_token: expiry (null = never) and granted permissions of <paramref name="inputToken"/>.</summary>
    private async Task<(DateTime? ExpiresAtUtc, HashSet<string> Scopes, string? Error)> DebugTokenAsync(string inputToken, string accessToken, CancellationToken ct)
    {
        var url = $"https://graph.facebook.com/{_graphApiVersion}/debug_token?input_token={Uri.EscapeDataString(inputToken)}&access_token={Uri.EscapeDataString(accessToken)}";
        var (ok, body, error) = await GetGraphAsync(url, ct);
        if (!ok || !body.TryGetProperty("data", out var data)) return (null, new HashSet<string>(), error ?? "Meta không trả lời.");
        if (data.TryGetProperty("is_valid", out var valid) && valid.ValueKind == JsonValueKind.False)
        {
            var msg = data.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m) ? m.GetString() : null;
            return (null, new HashSet<string>(), $"Meta báo mã không hợp lệ hoặc đã hết hạn. {msg}");
        }
        DateTime? expires = data.TryGetProperty("expires_at", out var ex) && ex.TryGetInt64(out var secs) && secs > 0
            ? DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime
            : null;
        var scopes = data.TryGetProperty("scopes", out var sc) && sc.ValueKind == JsonValueKind.Array
            ? sc.EnumerateArray().Select(s => s.GetString() ?? "").ToHashSet()
            : new HashSet<string>();
        return (expires, scopes, null);
    }

    private async Task<(string PageId, string Token)> PageTokenAsync(Guid facebookAccountId, CancellationToken ct)
    {
        var account = await _context.FacebookAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == facebookAccountId, ct);
        if (account == null || !account.IsActive || string.IsNullOrWhiteSpace(account.EncryptedAccessToken))
            throw new InvalidOperationException("Page chưa được kết nối hoặc đã ngắt kết nối. Vào Cài đặt → Facebook để kết nối lại.");
        return (account.PageId, _tokenProtection.DecryptToken(account.EncryptedAccessToken));
    }

    public async Task<string> PublishPhotoPostAsync(Guid facebookAccountId, IReadOnlyList<string> imagePaths, string message, CancellationToken cancellationToken = default)
    {
        if (imagePaths.Count == 0) throw new ArgumentException("Bài đăng cần ít nhất một ảnh.");
        var (pageId, token) = await PageTokenAsync(facebookAccountId, cancellationToken);
        var baseUrl = $"https://graph.facebook.com/{_graphApiVersion}/{pageId}";

        // 1. Upload every picture unpublished, keeping the order
        var mediaIds = new List<string>();
        foreach (var path in imagePaths)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(token), "access_token");
            form.Add(new StringContent("false"), "published");
            var bytes = new ByteArrayContent(await File.ReadAllBytesAsync(path, cancellationToken));
            bytes.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(bytes, "source", Path.GetFileName(path));
            using var res = await _httpClient.PostAsync($"{baseUrl}/photos", form, cancellationToken);
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Tải ảnh {mediaIds.Count + 1}/{imagePaths.Count} lên thất bại. {await ReadGraphErrorAsync(res, cancellationToken)}");
            var body = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            mediaIds.Add(body.GetProperty("id").GetString()!);
        }

        // 2. One feed post with all of them attached
        var fields = new List<KeyValuePair<string, string>>
        {
            new("access_token", token),
            new("message", message)
        };
        for (var i = 0; i < mediaIds.Count; i++)
            fields.Add(new($"attached_media[{i}]", JsonSerializer.Serialize(new { media_fbid = mediaIds[i] })));
        using var post = await _httpClient.PostAsync($"{baseUrl}/feed", new FormUrlEncodedContent(fields), cancellationToken);
        if (!post.IsSuccessStatusCode)
            throw new InvalidOperationException($"Đăng bài viết thất bại. {await ReadGraphErrorAsync(post, cancellationToken)}");
        var postBody = await post.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return postBody.GetProperty("id").GetString()!;
    }

    public async Task<string> PublishVideoStoryAsync(Guid facebookAccountId, string videoPath, CancellationToken cancellationToken = default)
    {
        var (pageId, token) = await PageTokenAsync(facebookAccountId, cancellationToken);
        var url = $"https://graph.facebook.com/{_graphApiVersion}/{pageId}/video_stories";

        using var start = await _httpClient.PostAsJsonAsync(url, new { upload_phase = "start", access_token = token }, cancellationToken);
        if (!start.IsSuccessStatusCode)
            throw new InvalidOperationException($"Khởi tạo Story thất bại. {await ReadGraphErrorAsync(start, cancellationToken)}");
        var startData = await start.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var videoId = startData.GetProperty("video_id").GetString()!;
        var uploadUrl = startData.GetProperty("upload_url").GetString()!;

        // Binary upload to rupload.facebook.com (offset + file_size headers, as for Reels)
        var size = new FileInfo(videoPath).Length;
        await using (var stream = File.OpenRead(videoPath))
        using (var upload = new HttpRequestMessage(HttpMethod.Post, uploadUrl))
        {
            upload.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
            upload.Headers.TryAddWithoutValidation("offset", "0");
            upload.Headers.TryAddWithoutValidation("file_size", size.ToString());
            upload.Content = new StreamContent(stream);
            upload.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var res = await _httpClient.SendAsync(upload, cancellationToken);
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Tải video Story lên thất bại. {await ReadGraphErrorAsync(res, cancellationToken)}");
        }

        using var finish = await _httpClient.PostAsJsonAsync(url, new { upload_phase = "finish", video_id = videoId, access_token = token }, cancellationToken);
        if (!finish.IsSuccessStatusCode)
            throw new InvalidOperationException($"Đăng Story thất bại. {await ReadGraphErrorAsync(finish, cancellationToken)}");
        var data = await finish.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return data.TryGetProperty("post_id", out var pid) && pid.GetString() is { Length: > 0 } postId ? postId : videoId;
    }

    public async Task<VideoMetricDto?> FetchMetricsAsync(Guid facebookPostId, CancellationToken cancellationToken = default)
    {
        var post = await _context.FacebookPosts
            .Include(p => p.FacebookAccount)
            .Include(p => p.Metrics)
            .FirstOrDefaultAsync(p => p.Id == facebookPostId, cancellationToken);

        if (post == null)
        {
            return null;
        }

        GraphVideoStats stats;
        if (post.FacebookAccount == null || string.IsNullOrWhiteSpace(post.FacebookVideoId))
        {
            stats = new GraphVideoStats { Error = "Bài đăng chưa có mã video Facebook hoặc trang đã bị ngắt kết nối." };
        }
        else
        {
            var rawToken = _tokenProtection.DecryptToken(post.FacebookAccount.EncryptedAccessToken);
            stats = await ReadVideoStatsAsync(rawToken, post.FacebookVideoId, post.FacebookPostId, cancellationToken);
        }

        // Never store zeros as if they were real numbers when Meta could not be reached
        if (stats.Error != null && !_simulate)
        {
            throw new InvalidOperationException($"Không lấy được số liệu từ Facebook. {stats.Error}");
        }

        // One snapshot per video per (Vietnam) day: later reads the same day update it, so the
        // history stays small and day-by-day view gains can be computed for the dashboard.
        var now = DateTime.UtcNow;
        var dayStartUtc = VietnamDayStartUtc(now);
        var metric = post.Metrics
            .Where(m => m.Status == "Recorded" && m.CollectedAtUtc >= dayStartUtc)
            .OrderByDescending(m => m.CollectedAtUtc)
            .FirstOrDefault();
        if (metric == null)
        {
            metric = new VideoMetric { FacebookPostId = post.Id, Status = "Recorded" };
            _context.VideoMetrics.Add(metric);
        }
        // Lifetime counters only go up: a lower reading (e.g. a metric briefly missing) keeps the old value
        var previous = post.Metrics.Where(m => m != metric).OrderByDescending(m => m.CollectedAtUtc).FirstOrDefault();
        metric.Views = Math.Max(stats.Views, previous?.Views ?? 0);
        metric.Reach = Math.Max(stats.Reach, previous?.Reach ?? 0);
        metric.Engagement = Math.Max(stats.Likes, previous?.Engagement ?? 0);
        metric.Shares = Math.Max(stats.Shares, previous?.Shares ?? 0);
        metric.Comments = Math.Max(stats.Comments, previous?.Comments ?? 0);
        metric.WatchTimeSeconds = stats.AvgWatchSeconds > 0 ? stats.AvgWatchSeconds : previous?.WatchTimeSeconds ?? 0;
        metric.CollectedAtUtc = now;
        await _context.SaveChangesAsync(cancellationToken);

        return new VideoMetricDto
        {
            Id = metric.Id,
            FacebookPostId = metric.FacebookPostId,
            Views = metric.Views,
            Reach = metric.Reach,
            Engagement = metric.Engagement,
            Shares = metric.Shares,
            Comments = metric.Comments,
            WatchTimeSeconds = metric.WatchTimeSeconds,
            CollectedAtUtc = metric.CollectedAtUtc
        };
    }

    /// <summary>Midnight (UTC+7) of the Vietnam day containing <paramref name="utc"/>, as UTC.</summary>
    public static DateTime VietnamDayStartUtc(DateTime utc) => utc.AddHours(7).Date.AddHours(-7);

    private sealed class GraphVideoStats
    {
        public long Views { get; set; }
        public long Reach { get; set; }
        public long Likes { get; set; }
        public long Comments { get; set; }
        public long Shares { get; set; }
        public double AvgWatchSeconds { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Reads a published Reel's lifetime numbers. Views come from the Reels insights
    /// (fb_reels_total_plays = plays incl. replays, needs the read_insights permission), then the classic
    /// video insights, then the video's plain "views" field as a last resort.
    /// </summary>
    private async Task<GraphVideoStats> ReadVideoStatsAsync(string token, string videoId, string? postId, CancellationToken ct)
    {
        var stats = new GraphVideoStats();
        var gotViews = false;
        string? insightsError = null;
        var baseUrl = $"https://graph.facebook.com/{_graphApiVersion}";
        try
        {
            // 1. Reels insights
            var reels = await GetGraphAsync($"{baseUrl}/{videoId}/video_insights?metric=fb_reels_total_plays,blue_reels_play_count,post_impressions_unique,post_video_avg_time_watched,post_video_social_actions&access_token={token}", ct);
            if (reels.Ok)
            {
                var values = InsightValues(reels.Body);
                if (values.ContainsKey("fb_reels_total_plays") || values.ContainsKey("blue_reels_play_count"))
                {
                    stats.Views = Math.Max(Number(values, "fb_reels_total_plays"), Number(values, "blue_reels_play_count"));
                    gotViews = true;
                }
                stats.Reach = Number(values, "post_impressions_unique");
                var avgMs = Number(values, "post_video_avg_time_watched");
                if (avgMs > 0) stats.AvgWatchSeconds = Math.Round(avgMs / 1000.0, 1);
                if (values.TryGetValue("post_video_social_actions", out var social) && social.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in social.EnumerateObject())
                    {
                        if (!prop.Value.TryGetInt64(out var n)) continue;
                        if (prop.Name.Contains("comment", StringComparison.OrdinalIgnoreCase)) stats.Comments = Math.Max(stats.Comments, n);
                        if (prop.Name.Contains("share", StringComparison.OrdinalIgnoreCase)) stats.Shares = Math.Max(stats.Shares, n);
                    }
                }
            }
            else
            {
                insightsError = reels.Error;
            }

            // 2. Classic video insights (videos that aren't Reels, or older API behaviour)
            if (!gotViews)
            {
                var video = await GetGraphAsync($"{baseUrl}/{videoId}/video_insights?metric=total_video_views,total_video_impressions_unique,total_video_avg_time_watched&access_token={token}", ct);
                if (video.Ok)
                {
                    var values = InsightValues(video.Body);
                    if (values.ContainsKey("total_video_views"))
                    {
                        stats.Views = Number(values, "total_video_views");
                        gotViews = true;
                    }
                    stats.Reach = Math.Max(stats.Reach, Number(values, "total_video_impressions_unique"));
                    var avgMs = Number(values, "total_video_avg_time_watched");
                    if (stats.AvgWatchSeconds == 0 && avgMs > 0) stats.AvgWatchSeconds = Math.Round(avgMs / 1000.0, 1);
                }
                else
                {
                    insightsError ??= video.Error;
                }
            }

            // 3. Plain "views" field (works with pages_read_engagement only on some videos)
            if (!gotViews)
            {
                var field = await GetGraphAsync($"{baseUrl}/{videoId}?fields=views&access_token={token}", ct);
                if (field.Ok && field.Body.TryGetProperty("views", out var v) && v.TryGetInt64(out var vw))
                {
                    stats.Views = vw;
                    gotViews = true;
                }
            }

            // Likes and comments from the video object
            var counts = await GetGraphAsync($"{baseUrl}/{videoId}?fields=likes.summary(true),comments.summary(true)&access_token={token}", ct);
            if (counts.Ok)
            {
                stats.Likes = SummaryCount(counts.Body, "likes");
                stats.Comments = Math.Max(stats.Comments, SummaryCount(counts.Body, "comments"));
            }

            // Shares from the post
            if (!string.IsNullOrWhiteSpace(postId))
            {
                var shares = await GetGraphAsync($"{baseUrl}/{postId}?fields=shares&access_token={token}", ct);
                if (shares.Ok && shares.Body.TryGetProperty("shares", out var sp) && sp.TryGetProperty("count", out var sc) && sc.TryGetInt64(out var n))
                {
                    stats.Shares = Math.Max(stats.Shares, n);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Reading Reel stats for video {VideoId} failed: {Message}", videoId, ex.Message);
            insightsError = "Không kết nối được tới Meta Graph API: " + ex.Message;
        }

        if (!gotViews)
        {
            var permission = insightsError != null
                && (insightsError.Contains("permission", StringComparison.OrdinalIgnoreCase)
                    || insightsError.Contains("read_insights", StringComparison.OrdinalIgnoreCase)
                    || insightsError.Contains("(#10)") || insightsError.Contains("(#200)"));
            stats.Error = permission
                ? "Token của Page chưa có quyền xem thống kê (read_insights). Hãy tạo lại Page Access Token có thêm quyền read_insights và pages_read_engagement rồi kết nối lại Page. Chi tiết: " + insightsError
                : insightsError ?? "Meta không trả về lượt xem cho video này.";
        }
        return stats;
    }

    private async Task<(bool Ok, JsonElement Body, string? Error)> GetGraphAsync(string url, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return (false, default, await ReadGraphErrorAsync(response, ct));
        return (true, await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct), null);
    }

    /// <summary>metric name → value of its first (lifetime) entry.</summary>
    private static Dictionary<string, JsonElement> InsightValues(JsonElement body)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in data.EnumerateArray())
        {
            if (item.TryGetProperty("name", out var name) && name.GetString() is { } key
                && item.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Array && vals.GetArrayLength() > 0
                && vals[0].TryGetProperty("value", out var value))
            {
                result[key] = value.Clone();
            }
        }
        return result;
    }

    private static long Number(Dictionary<string, JsonElement> values, string key)
    {
        if (!values.TryGetValue(key, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt64(out var n) ? n : (long)v.GetDouble();
        if (v.ValueKind == JsonValueKind.Object)
            return v.EnumerateObject().Sum(p => p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt64(out var x) ? x : 0);
        return 0;
    }

    private static long SummaryCount(JsonElement body, string field) =>
        body.TryGetProperty(field, out var f) && f.TryGetProperty("summary", out var s)
        && s.TryGetProperty("total_count", out var c) && c.TryGetInt64(out var n) ? n : 0;

    public async Task<List<FacebookPostDto>> GetProjectPostsAsync(Guid videoProjectId, CancellationToken cancellationToken = default)
    {
        var posts = await _context.FacebookPosts
            .Include(p => p.FacebookAccount)
            .Include(p => p.Metrics)
            .Include(p => p.InstagramMetrics)
            .Where(p => p.VideoProjectId == videoProjectId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return posts.Select(p =>
        {
            var latestMetric = p.Metrics.OrderByDescending(m => m.CollectedAtUtc).FirstOrDefault();
            return new FacebookPostDto
            {
                Id = p.Id,
                VideoProjectId = p.VideoProjectId,
                FacebookAccountId = p.FacebookAccountId,
                PageName = p.FacebookAccount?.PageName,
                FacebookPostId = p.FacebookPostId,
                FacebookVideoId = p.FacebookVideoId,
                PostUrl = !string.IsNullOrWhiteSpace(p.FacebookVideoId) ? $"https://www.facebook.com/reel/{p.FacebookVideoId}" : null,
                UploadStatus = p.UploadStatus,
                PublishedAtUtc = p.PublishedAtUtc,
                Status = p.Status,
                ErrorMessage = p.ErrorMessage,
                FirstCommentId = p.FirstCommentId,
                FirstCommentError = p.FirstCommentError,
                InstagramPermalink = p.InstagramPermalink,
                InstagramError = p.InstagramError,
                InstagramViews = p.InstagramMetrics.Count > 0 ? p.InstagramMetrics.Max(m => m.Views) : null,
                InstagramLikes = p.InstagramMetrics.Count > 0 ? p.InstagramMetrics.Max(m => m.Likes) : null,
                InstagramComments = p.InstagramMetrics.Count > 0 ? p.InstagramMetrics.Max(m => m.Comments) : null,
                LatestMetric = latestMetric != null ? new VideoMetricDto
                {
                    Id = latestMetric.Id,
                    FacebookPostId = latestMetric.FacebookPostId,
                    Views = latestMetric.Views,
                    Reach = latestMetric.Reach,
                    Engagement = latestMetric.Engagement,
                    Shares = latestMetric.Shares,
                    Comments = latestMetric.Comments,
                    WatchTimeSeconds = latestMetric.WatchTimeSeconds,
                    CollectedAtUtc = latestMetric.CollectedAtUtc
                } : null
            };
        }).ToList();
    }
}
