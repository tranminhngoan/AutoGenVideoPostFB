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
            CreatedAtUtc = a.CreatedAtUtc
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
            EphemeralAssetsCleanedUp = cleanedUp
        };
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

        var views = 0L;
        var reach = 0L;
        var engagement = 0L;
        var shares = 0L;
        var comments = 0L;
        var watchTime = 0.0;
        string? insightsError = null;

        if (post.FacebookAccount == null || string.IsNullOrWhiteSpace(post.FacebookVideoId))
        {
            insightsError = "Bài đăng chưa có mã video Facebook hoặc trang đã bị ngắt kết nối.";
        }
        else
        {
            var rawToken = _tokenProtection.DecryptToken(post.FacebookAccount.EncryptedAccessToken);
            var apiSucceeded = false;
            try
            {
                // 1. Lấy số liệu trực tiếp từ đối tượng Video (views, likes, comments)
                var videoFieldsUrl = $"https://graph.facebook.com/{_graphApiVersion}/{post.FacebookVideoId}?fields=views,likes.summary(true),comments.summary(true)&access_token={rawToken}";
                var videoRes = await _httpClient.GetAsync(videoFieldsUrl, cancellationToken);
                if (videoRes.IsSuccessStatusCode)
                {
                    apiSucceeded = true;
                    var vDoc = await videoRes.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                    if (vDoc.TryGetProperty("views", out var viewsProp) && viewsProp.TryGetInt64(out var vw))
                    {
                        views = vw;
                    }
                    if (vDoc.TryGetProperty("likes", out var likesProp) && likesProp.TryGetProperty("summary", out var lSummary) && lSummary.TryGetProperty("total_count", out var lCount) && lCount.TryGetInt64(out var lc))
                    {
                        engagement = lc;
                    }
                    if (vDoc.TryGetProperty("comments", out var commProp) && commProp.TryGetProperty("summary", out var cSummary) && cSummary.TryGetProperty("total_count", out var cCount) && cCount.TryGetInt64(out var cc))
                    {
                        comments = cc;
                    }
                }

                // 2. Lấy số lượt chia sẻ nếu có FacebookPostId
                if (!string.IsNullOrWhiteSpace(post.FacebookPostId))
                {
                    var postFieldsUrl = $"https://graph.facebook.com/{_graphApiVersion}/{post.FacebookPostId}?fields=shares&access_token={rawToken}";
                    var postRes = await _httpClient.GetAsync(postFieldsUrl, cancellationToken);
                    if (postRes.IsSuccessStatusCode)
                    {
                        var pDoc = await postRes.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                        if (pDoc.TryGetProperty("shares", out var sharesProp) && sharesProp.TryGetProperty("count", out var sCount) && sCount.TryGetInt64(out var sc))
                        {
                            shares = sc;
                        }
                    }
                }

                // 3. Lấy thêm chỉ số video_insights nếu khả dụng (chỉ dùng các metric hợp lệ cho video)
                var insightsUrl = $"https://graph.facebook.com/{_graphApiVersion}/{post.FacebookVideoId}/video_insights?metric=total_video_views,total_video_avg_time_watched&access_token={rawToken}";
                var response = await _httpClient.GetAsync(insightsUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    apiSucceeded = true;
                    var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                    if (doc.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in dataArray.EnumerateArray())
                        {
                            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                            if (item.TryGetProperty("values", out var vals) && vals.GetArrayLength() > 0)
                            {
                                var valObj = vals[0];
                                if (valObj.TryGetProperty("value", out var v))
                                {
                                    if (name == "total_video_views" && v.TryGetInt64(out var vw) && vw > views) views = vw;
                                    if (name == "total_video_avg_time_watched" && v.TryGetDouble(out var wt)) watchTime = wt;
                                }
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("video_insights endpoint returned non-success ({StatusCode}). Continuing with object-level metrics.", response.StatusCode);
                }

                // Reach ước tính từ Views
                reach = Math.Max(reach, views);

                if (!apiSucceeded)
                {
                    insightsError = await ReadGraphErrorAsync(response, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Insights API call could not be completed: {Message}", ex.Message);
                if (!apiSucceeded)
                {
                    insightsError = "Không kết nối được tới Meta Graph API: " + ex.Message;
                }
            }
        }

        // Never store zeros as if they were real numbers when Meta could not be reached
        if (insightsError != null && !_simulate)
        {
            throw new InvalidOperationException($"Không lấy được số liệu từ Facebook. {insightsError}");
        }

        var metric = new VideoMetric
        {
            FacebookPostId = post.Id,
            Views = views,
            Reach = reach,
            Engagement = engagement,
            Shares = shares,
            Comments = comments,
            WatchTimeSeconds = watchTime,
            CollectedAtUtc = DateTime.UtcNow,
            Status = "Recorded"
        };
        _context.VideoMetrics.Add(metric);
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

    public async Task<List<FacebookPostDto>> GetProjectPostsAsync(Guid videoProjectId, CancellationToken cancellationToken = default)
    {
        var posts = await _context.FacebookPosts
            .Include(p => p.FacebookAccount)
            .Include(p => p.Metrics)
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
