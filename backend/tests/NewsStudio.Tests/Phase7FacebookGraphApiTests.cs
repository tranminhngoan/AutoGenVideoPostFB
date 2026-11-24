using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Api;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Domain.Enums;
using Xunit;

namespace NewsStudio.Tests;

[Collection("DatabaseCollection")]
public class Phase7FacebookGraphApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public Phase7FacebookGraphApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public void TokenProtection_Encrypts_Decrypts_And_Masks_Token_Safely()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var tokenProtection = scope.ServiceProvider.GetRequiredService<IFacebookTokenProtectionService>();
        var originalToken = "EAAB1234567890ABCDEF1234567890XYZ";

        // Act
        var encrypted = tokenProtection.EncryptToken(originalToken);
        var decrypted = tokenProtection.DecryptToken(encrypted);
        var masked = tokenProtection.MaskToken(originalToken);

        // Assert
        encrypted.Should().NotBeNullOrWhiteSpace();
        encrypted.Should().NotBe(originalToken); // Ciphertext is protected
        decrypted.Should().Be(originalToken);    // Decrypted matches exactly
        masked.Should().StartWith("EAAB");
        masked.Should().EndWith("0XYZ");
        masked.Should().Contain("••••••••");
        masked.Should().NotContain("1234567890ABCDEF"); // Internal characters masked
    }

    [Fact]
    public async Task Page_stats_follow_studio_and_hand_made_posts_and_followers()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var stats = scope.ServiceProvider.GetRequiredService<IPageStatsService>();
        var page = new FacebookAccount { PageId = $"page_{Guid.NewGuid():N}", PageName = "Trạm ký ức", EncryptedAccessToken = "x", IsActive = false };
        db.FacebookAccounts.Add(page);
        var studio = new FacebookPost { FacebookAccountId = page.Id, Source = "Studio", Title = "Bát tóp mỡ", FacebookVideoId = "111",
            Status = "Published", UploadStatus = "Published", PublishedAtUtc = DateTime.UtcNow.AddHours(-3) };
        var byHand = new FacebookPost { FacebookAccountId = page.Id, Source = "Discovered", Title = "Reel đăng tay", InstagramMediaId = "ig1",
            Status = "Published", UploadStatus = "Published", PublishedAtUtc = DateTime.UtcNow.AddHours(-1) };
        db.FacebookPosts.Add(studio);
        db.FacebookPosts.Add(byHand);
        db.VideoMetrics.Add(new VideoMetric { FacebookPostId = studio.Id, Status = "Recorded", Views = 120, Engagement = 9, Comments = 3, CollectedAtUtc = DateTime.UtcNow });
        db.InstagramMetrics.Add(new InstagramMetric { FacebookPostId = byHand.Id, Views = 450, Likes = 30, Comments = 4, CollectedAtUtc = DateTime.UtcNow });
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        db.PageFollowerSnapshots.Add(new PageFollowerSnapshot { FacebookAccountId = page.Id, Date = today.AddDays(-8), FacebookFollowers = 100, InstagramFollowers = 40, CollectedAtUtc = DateTime.UtcNow.AddDays(-8) });
        db.PageFollowerSnapshots.Add(new PageFollowerSnapshot { FacebookAccountId = page.Id, Date = today, FacebookFollowers = 130, InstagramFollowers = 55, CollectedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var overview = await stats.GetOverviewAsync(14);
        var dto = overview.Pages.Single(p => p.FacebookAccountId == page.Id);
        dto.Videos.Should().Be(1);                                   // the Instagram-only post is not a Facebook video
        dto.Followers.Should().Be(130);
        dto.FollowersChange7Days.Should().Be(30);
        dto.InstagramFollowersChange7Days.Should().Be(15);
        dto.RecentPosts.Select(p => p.Title).Should().Equal("Reel đăng tay", "Bát tóp mỡ"); // newest first
        dto.RecentPosts[1].Views.Should().Be(120);
        dto.RecentPosts[1].Source.Should().Be("Studio");
        dto.RecentPosts[0].InstagramViews.Should().Be(450);
        dto.TopVideo!.Title.Should().Be("Bát tóp mỡ");

        db.FacebookPosts.RemoveRange(studio, byHand);
        db.PageFollowerSnapshots.RemoveRange(db.PageFollowerSnapshots.Where(f => f.FacebookAccountId == page.Id));
        db.FacebookAccounts.Remove(page);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Deleting_a_page_removes_its_settings_but_keeps_videos()
    {
        using var scope = _factory.Services.CreateScope();
        var facebookService = scope.ServiceProvider.GetRequiredService<IFacebookPublishingService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var page = new FacebookAccount { PageId = $"page_{Guid.NewGuid():N}", PageName = "Coi chừng nghiện", EncryptedAccessToken = "x", IsActive = false };
        db.FacebookAccounts.Add(page);
        var settings = new AutoPostSettings { FacebookAccountId = page.Id };
        db.AutoPostSettings.Add(settings);
        var video = new StudioVideo { Title = "Kem mút", FacebookAccountId = page.Id };
        db.StudioVideos.Add(video);
        await db.SaveChangesAsync();

        (await facebookService.DeletePageAsync(page.Id)).Should().BeTrue();

        using var check = _factory.Services.CreateScope();
        var fresh = check.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        fresh.FacebookAccounts.Any(a => a.Id == page.Id).Should().BeFalse();
        fresh.AutoPostSettings.Any(s => s.Id == settings.Id).Should().BeFalse();
        var kept = fresh.StudioVideos.Single(v => v.Id == video.Id);
        kept.FacebookAccountId.Should().BeNull();                       // the video stays, just unlinked
        fresh.StudioVideos.Remove(kept);
        await fresh.SaveChangesAsync();
        (await facebookService.DeletePageAsync(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task FacebookPublishingService_Connects_And_Retrieves_Pages_Safely()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var facebookService = scope.ServiceProvider.GetRequiredService<IFacebookPublishingService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var pageId = $"page_{Guid.NewGuid():N}";
        var rawToken = "EAAB_test_page_access_token_secure_987654";

        // Act: Connect Page
        var connected = await facebookService.ConnectPageAsync(new ConnectFacebookPageRequestDto
        {
            PageId = pageId,
            PageName = "Báo Tin Nhanh 247",
            PageAccessToken = rawToken
        });

        // Assert
        connected.Should().NotBeNull();
        connected.PageId.Should().Be(pageId);
        connected.PageName.Should().Be("Báo Tin Nhanh 247");
        connected.HasToken.Should().BeTrue();
        connected.MaskedToken.Should().Contain("••••••••");

        // Verify in PostgreSQL: Token MUST be encrypted, NOT stored in plain text
        var accountInDb = db.FacebookAccounts.FirstOrDefault(a => a.PageId == pageId);
        accountInDb.Should().NotBeNull();
        accountInDb!.EncryptedAccessToken.Should().NotBe(rawToken);

        // Act: Get Connected Pages
        var pages = await facebookService.GetConnectedPagesAsync();
        pages.Should().Contain(p => p.PageId == pageId);
    }

    [Fact]
    public async Task FacebookPublishingService_Blocks_Publish_If_Video_Not_Approved()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var facebookService = scope.ServiceProvider.GetRequiredService<IFacebookPublishingService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var project = new VideoProject
        {
            Title = "Dự án chưa được phê duyệt",
            SourceUrl = "https://example.com/unapproved-news",
            Status = VideoProjectStatus.AwaitingApproval
            // No VideoReview with Decision = "Approved"
        };
        db.VideoProjects.Add(project);
        await db.SaveChangesAsync();

        // Act & Assert: Must throw InvalidOperationException
        Func<Task> act = async () => await facebookService.PublishReelAsync(project.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*chưa được ban biên tập phê duyệt*");
    }

    [Fact]
    public async Task FacebookPublishingService_Prevents_Duplicate_Publishing_Idempotency()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var facebookService = scope.ServiceProvider.GetRequiredService<IFacebookPublishingService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var project = new VideoProject
        {
            Title = "Dự án đã từng được đăng Reels",
            SourceUrl = "https://example.com/duplicate-test",
            Status = VideoProjectStatus.Published
        };
        db.VideoProjects.Add(project);

        // Add Approved Review
        db.VideoReviews.Add(new VideoReview
        {
            VideoProjectId = project.Id,
            Decision = "Approved",
            Status = "Reviewed"
        });

        // Add Existing Published Post Record
        var existingPost = new FacebookPost
        {
            VideoProjectId = project.Id,
            FacebookPostId = "100200_99887766",
            FacebookVideoId = "99887766",
            UploadStatus = "Published",
            PublishedAtUtc = DateTime.UtcNow.AddHours(-3),
            Status = "Published"
        };
        db.FacebookPosts.Add(existingPost);
        await db.SaveChangesAsync();

        // Act: Try publishing again
        var result = await facebookService.PublishReelAsync(project.Id);

        // Assert: Idempotently returns existing published post without duplicate
        result.Should().NotBeNull();
        result.UploadStatus.Should().Be("Published");
        result.FacebookPostId.Should().Be("100200_99887766");
        result.FacebookVideoId.Should().Be("99887766");
    }

    [Fact]
    public async Task FacebookPublishingService_Publishes_Reel_And_Performs_Ephemeral_Cleanup()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var facebookService = scope.ServiceProvider.GetRequiredService<IFacebookPublishingService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var project = new VideoProject
        {
            Title = "TIÊU ĐIỂM: Khánh thành cầu Mỹ Thuận 2",
            SourceUrl = "https://example.com/cau-my-thuan-2",
            Publisher = "Báo Tuổi Trẻ",
            Status = VideoProjectStatus.AwaitingApproval
        };
        db.VideoProjects.Add(project);

        var script = new VideoScript
        {
            VideoProjectId = project.Id,
            Title = project.Title,
            Hook = "Cầu Mỹ Thuận 2 chính thức nối liền hai bờ Tiền Giang và Vĩnh Long!",
            VoiceOverScript = "Toàn bộ công trình dài 6.6 km đã thông xe, giải quyết dứt điểm nút thắt cổ chai giao thông miền Tây.",
            SourceAttribution = "Theo Báo Tuổi Trẻ"
        };
        db.VideoScripts.Add(script);

        // Review approved
        db.VideoReviews.Add(new VideoReview
        {
            VideoProjectId = project.Id,
            Decision = "Approved",
            Status = "Reviewed"
        });

        // Create ephemeral video file on disk
        var tempVideoPath = Path.Combine(Path.GetTempPath(), $"publish_test_{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(tempVideoPath, new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 });

        db.VideoAssets.Add(new VideoAsset
        {
            VideoProjectId = project.Id,
            AssetType = "FinalVideo",
            LocalTempPath = tempVideoPath,
            FileSizeBytes = 8,
            MimeType = "video/mp4",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(2),
            IsCleanedUp = false,
            Status = "Active"
        });
        await db.SaveChangesAsync();

        // Act: Publish with AutoCleanupAssets = true
        var result = await facebookService.PublishReelAsync(project.Id, new PublishReelRequestDto
        {
            AutoCleanupAssets = true
        });

        // Assert
        result.Should().NotBeNull();
        result.UploadStatus.Should().Be("Published");
        result.FacebookVideoId.Should().NotBeNullOrWhiteSpace();
        result.PostUrl.Should().Contain("facebook.com/reel/");
        result.EphemeralAssetsCleanedUp.Should().BeTrue();

        // Verify PostgreSQL updates
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var updatedProject = await verifyDb.VideoProjects.FindAsync(project.Id);
        updatedProject!.Status.Should().Be(VideoProjectStatus.Published);

        var postInDb = verifyDb.FacebookPosts.FirstOrDefault(p => p.VideoProjectId == project.Id);
        postInDb.Should().NotBeNull();
        postInDb!.UploadStatus.Should().Be("Published");
        postInDb.PublishedAtUtc.Should().NotBeNull();

        // Verify Ephemeral Storage rule: File was deleted after publishing
        File.Exists(tempVideoPath).Should().BeFalse();
    }

    [Fact]
    public async Task FacebookPublishingService_Fetches_Metrics_Without_Fabrication()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var facebookService = scope.ServiceProvider.GetRequiredService<IFacebookPublishingService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var project = new VideoProject
        {
            Title = "Dự án kiểm tra Insights Metrics",
            SourceUrl = "https://example.com/insights-test",
            Status = VideoProjectStatus.Published
        };
        db.VideoProjects.Add(project);

        var post = new FacebookPost
        {
            VideoProjectId = project.Id,
            FacebookPostId = "100200_334455",
            FacebookVideoId = "334455",
            UploadStatus = "Published",
            PublishedAtUtc = DateTime.UtcNow.AddDays(-1),
            Status = "Published"
        };
        db.FacebookPosts.Add(post);
        await db.SaveChangesAsync();

        // Act: Fetch metrics
        var metricDto = await facebookService.FetchMetricsAsync(post.Id);

        // Assert
        metricDto.Should().NotBeNull();
        metricDto!.FacebookPostId.Should().Be(post.Id);
        metricDto.Views.Should().BeGreaterOrEqualTo(0);
        metricDto.Reach.Should().BeGreaterOrEqualTo(0);
        metricDto.Engagement.Should().BeGreaterOrEqualTo(0);
        metricDto.CollectedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task FacebookController_Endpoints_Full_Workflow()
    {
        // 1. Act: POST /api/facebook/verify
        var verifyRes = await _client.PostAsJsonAsync("/api/facebook/verify", new ConnectFacebookPageRequestDto
        {
            PageAccessToken = "EAAB_test_mock_token_12345"
        });
        verifyRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyDto = await verifyRes.Content.ReadFromJsonAsync<VerifyTokenResultDto>();
        verifyDto.Should().NotBeNull();
        verifyDto!.IsValid.Should().BeTrue();

        // 2. Act: POST /api/facebook/pages
        var pageId = $"test_page_{Guid.NewGuid():N}";
        var connectRes = await _client.PostAsJsonAsync("/api/facebook/pages", new ConnectFacebookPageRequestDto
        {
            PageId = pageId,
            PageName = "Trang Báo Test Workflow",
            PageAccessToken = "EAAB_test_token_workflow_67890"
        });
        connectRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var pageDto = await connectRes.Content.ReadFromJsonAsync<FacebookAccountDto>();
        pageDto.Should().NotBeNull();
        pageDto!.PageId.Should().Be(pageId);
        pageDto.MaskedToken.Should().Contain("••••••••");

        // 3. Act: GET /api/facebook/pages
        var getPagesRes = await _client.GetAsync("/api/facebook/pages");
        getPagesRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Arrange approved project for publishing
        using (var setupScope = _factory.Services.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var project = new VideoProject
            {
                Title = "Dự án test publish qua Controller",
                SourceUrl = "https://example.com/test-controller",
                Status = VideoProjectStatus.AwaitingApproval
            };
            db.VideoProjects.Add(project);

            db.VideoReviews.Add(new VideoReview
            {
                VideoProjectId = project.Id,
                Decision = "Approved",
                Status = "Reviewed"
            });
            await db.SaveChangesAsync();

            // 5. Act: POST /api/video-projects/{id}/publish-reel
            var publishRes = await _client.PostAsJsonAsync($"/api/video-projects/{project.Id}/publish-reel", new PublishReelRequestDto
            {
                PageId = pageId,
                CustomCaption = "Caption test qua API"
            });
            publishRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var pubDto = await publishRes.Content.ReadFromJsonAsync<PublishReelResultDto>();
            pubDto.Should().NotBeNull();
            pubDto!.UploadStatus.Should().Be("Published");

            // 6. Act: GET /api/video-projects/{id}/facebook-posts
            var postsRes = await _client.GetAsync($"/api/video-projects/{project.Id}/facebook-posts");
            postsRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var postsList = await postsRes.Content.ReadFromJsonAsync<List<FacebookPostDto>>();
            postsList.Should().NotBeNullOrEmpty();

            // 7. Act: GET /api/facebook/posts/{postId}/metrics
            var metricsRes = await _client.GetAsync($"/api/facebook/posts/{postsList!.First().Id}/metrics");
            metricsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // 8. Act: DELETE /api/facebook/pages/{id}
        var disconnectRes = await _client.DeleteAsync($"/api/facebook/pages/{pageDto!.Id}");
        disconnectRes.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
