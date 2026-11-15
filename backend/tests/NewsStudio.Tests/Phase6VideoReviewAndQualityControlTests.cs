using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Domain.Enums;
using Xunit;

using Microsoft.AspNetCore.Mvc.Testing;
using NewsStudio.Api;

namespace NewsStudio.Tests;

[Collection("DatabaseCollection")]
public class Phase6VideoReviewAndQualityControlTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public Phase6VideoReviewAndQualityControlTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task QualityController_Evaluates_All_10_Criteria_Successfully()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var qualityController = scope.ServiceProvider.GetRequiredService<IQualityControllerService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var article = new Article
        {
            Title = "Thông xe cầu vượt ngã tư Thủ Đức giai đoạn 2",
            NormalizedTitle = "thong xe cau vuot nga tu thu duc giai doan 2",
            Url = $"https://test-news.vn/cau-vuot-thu-duc-{Guid.NewGuid()}",
            Publisher = "Báo Tuổi Trẻ",
            ContentBody = "Sáng nay, cầu vượt ngã tư Thủ Đức chính thức thông xe toàn tuyến giúp giải tỏa ùn tắc giao thông cửa ngõ phía Đông TP.HCM.",
            Status = "Extracted"
        };
        db.Articles.Add(article);
        await db.SaveChangesAsync();

        var project = new VideoProject
        {
            Title = "TIÊU ĐIỂM: Thông xe cầu vượt Thủ Đức",
            SourceUrl = article.Url,
            Publisher = "Báo Tuổi Trẻ",
            ArticleId = article.Id,
            Status = VideoProjectStatus.Rendering,
            TargetDurationSeconds = 52,
            ActualDurationSeconds = 51.5
        };
        db.VideoProjects.Add(project);
        await db.SaveChangesAsync();

        var script = new VideoScript
        {
            VideoProjectId = project.Id,
            Title = project.Title,
            Hook = "Cầu vượt ngã tư Thủ Đức chính thức thông xe sáng nay!",
            VoiceOverScript = "Sáng nay, cầu vượt ngã tư Thủ Đức chính thức thông xe toàn tuyến, giải quyết dứt điểm điểm đen ùn tắc giao thông cửa ngõ phía Đông thành phố.",
            KeyFacts = "- Cầu vượt dài hơn 500m.\n- Thông xe toàn tuyến hôm nay.",
            Context = "Cửa ngõ phía Đông thường xuyên ùn tắc giờ cao điểm.",
            Analysis = "Dự án hoàn thành sớm 2 tháng, mở toang cửa ngõ liên kết vùng Đông Nam Bộ.",
            Conclusion = "Bạn đánh giá thế nào về diện mạo mới của giao thông khu vực này?",
            SourceAttribution = "Theo Báo Tuổi Trẻ"
        };
        db.VideoScripts.Add(script);

        // Add 6 scenes with Typography & OnScreenText
        for (int i = 1; i <= 6; i++)
        {
            db.VideoScenes.Add(new VideoScene
            {
                VideoProjectId = project.Id,
                SceneNumber = i,
                StartTimeSeconds = (i - 1) * 8.5,
                EndTimeSeconds = i * 8.5,
                VoiceText = $"Đoạn thuyết minh phân cảnh số {i}",
                OnScreenText = $"TIÊU ĐIỂM #{i}",
                MotionType = "ZoomIn",
                TransitionType = "Fade",
                ScenePurpose = "Fact"
            });
        }

        // Add valid ephemeral assets
        db.VideoAssets.Add(new VideoAsset
        {
            VideoProjectId = project.Id,
            AssetType = "Audio",
            LocalTempPath = "C:/tmp/test_voice.wav",
            FileSizeBytes = 120000,
            MimeType = "audio/wav",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(2),
            IsCleanedUp = false,
            Status = "Active"
        });
        db.VideoAssets.Add(new VideoAsset
        {
            VideoProjectId = project.Id,
            AssetType = "FinalVideo",
            LocalTempPath = "C:/tmp/test_reel.mp4",
            FileSizeBytes = 1500000,
            MimeType = "video/mp4",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(2),
            IsCleanedUp = false,
            Status = "Active"
        });
        db.VideoAssets.Add(new VideoAsset
        {
            VideoProjectId = project.Id,
            AssetType = "Subtitle",
            LocalTempPath = "C:/tmp/test_sub.srt",
            FileSizeBytes = 1500,
            MimeType = "text/plain",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(2),
            IsCleanedUp = false,
            Status = "Active"
        });
        await db.SaveChangesAsync();

        // Act
        var result = await qualityController.EvaluateProjectQualityAsync(project.Id);

        // Assert
        result.Should().NotBeNull();
        result.CheckItems.Should().HaveCount(10);
        result.TotalScore.Should().BeGreaterOrEqualTo(70);
        result.HasFatalViolations.Should().BeFalse();
        result.CanPublish.Should().BeTrue();
        result.Summary.Should().NotBeNullOrWhiteSpace();
        result.Notice.Should().Contain("Facebook");
    }

    [Fact]
    public async Task QualityController_Detects_Fatal_Violations_When_Image_Is_Rejected()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var qualityController = scope.ServiceProvider.GetRequiredService<IQualityControllerService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var article = new Article
        {
            Title = "Dự án kiểm định ảnh vi phạm bản quyền",
            NormalizedTitle = "du an kiem dinh anh vi pham ban quyen",
            Url = $"https://test-news.vn/vi-pham-{Guid.NewGuid()}",
            Publisher = "Báo Mới",
            ContentBody = "Bài viết kiểm tra kiểm định chất lượng nghiêm ngặt.",
            Status = "Extracted"
        };
        db.Articles.Add(article);
        await db.SaveChangesAsync();

        var rejectedImage = new ArticleImage
        {
            ArticleId = article.Id,
            OriginalImageUrl = "https://example.com/unauthorized.jpg",
            RightsStatus = RightsStatus.Rejected, // EXPLICITLY REJECTED
            Category = ImageCategory.Unsuitable,
            ArticleImageOrder = 1
        };
        db.ArticleImages.Add(rejectedImage);
        await db.SaveChangesAsync();

        var project = new VideoProject
        {
            Title = "Dự án vi phạm bản quyền ảnh",
            SourceUrl = article.Url,
            ArticleId = article.Id,
            Status = VideoProjectStatus.Rendering
        };
        db.VideoProjects.Add(project);
        await db.SaveChangesAsync();

        // Assign the REJECTED image to Scene #1
        db.VideoScenes.Add(new VideoScene
        {
            VideoProjectId = project.Id,
            SceneNumber = 1,
            StartTimeSeconds = 0,
            EndTimeSeconds = 8.5,
            VoiceText = "Cảnh mở đầu dùng ảnh vi phạm",
            OnScreenText = "VI PHẠM BẢN QUYỀN",
            SelectedArticleImageId = rejectedImage.Id,
            ScenePurpose = "Hook"
        });
        await db.SaveChangesAsync();

        // Act
        var result = await qualityController.EvaluateProjectQualityAsync(project.Id);

        // Assert
        result.HasFatalViolations.Should().BeTrue();
        result.CanPublish.Should().BeFalse();
        result.FatalViolations.Should().Contain(v => v.Contains("từ chối") || v.Contains("Rejected"));

        var rightsCheck = result.CheckItems.First(c => c.CheckCode == "QC_05_IMAGE_RIGHTS");
        rightsCheck.Passed.Should().BeFalse();
        rightsCheck.IsFatal.Should().BeTrue();
    }

    [Fact]
    public async Task VideoReviewService_Blocks_Approval_When_Fatal_Violations_Exist()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var reviewService = scope.ServiceProvider.GetRequiredService<IVideoReviewService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var article = new Article
        {
            Title = "Dự án chặn phê duyệt do thiếu kịch bản",
            NormalizedTitle = "du an chan phe duyet do thieu kich ban",
            Url = $"https://test-news.vn/chan-duyet-{Guid.NewGuid()}",
            Publisher = "Báo Test",
            ContentBody = "Nội dung bài viết",
            Status = "Extracted"
        };
        db.Articles.Add(article);
        await db.SaveChangesAsync();

        var project = new VideoProject
        {
            Title = "Dự án không có kịch bản",
            SourceUrl = article.Url,
            ArticleId = article.Id,
            Status = VideoProjectStatus.AwaitingApproval
            // Script is intentionally null -> Fatal Violation QC_01
        };
        db.VideoProjects.Add(project);
        await db.SaveChangesAsync();

        // Act & Assert: Approval MUST be blocked with InvalidOperationException
        Func<Task> act = async () => await reviewService.SubmitDecisionAsync(project.Id, new SubmitReviewDecisionRequestDto
        {
            Decision = "Approved",
            ReviewedBy = "Editor",
            FeedbackNotes = "Thử duyệt khi chưa đủ điều kiện"
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*lỗi nghiêm trọng chặn xuất bản*");
    }

    [Fact]
    public async Task VideoReviewService_Submits_Approval_Successfully_When_Standards_Met()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var reviewService = scope.ServiceProvider.GetRequiredService<IVideoReviewService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var article = new Article
        {
            Title = "Khởi công cao tốc Châu Đốc Cần Thơ Sóc Trăng",
            NormalizedTitle = "khoi cong cao toc chau doc can tho soc trang",
            Url = $"https://test-news.vn/cao-toc-{Guid.NewGuid()}",
            Publisher = "Báo Đầu Tư",
            ContentBody = "Cao tốc Châu Đốc Cần Thơ Sóc Trăng kết nối miền Tây với tổng mức đầu tư hàng chục nghìn tỷ đồng.",
            Status = "Extracted"
        };
        db.Articles.Add(article);
        await db.SaveChangesAsync();

        var project = new VideoProject
        {
            Title = "TIÊU ĐIỂM: Cao tốc trục ngang miền Tây",
            SourceUrl = article.Url,
            Publisher = "Báo Đầu Tư",
            ArticleId = article.Id,
            Status = VideoProjectStatus.AwaitingApproval,
            TargetDurationSeconds = 50,
            ActualDurationSeconds = 49.5
        };
        db.VideoProjects.Add(project);
        await db.SaveChangesAsync();

        var script = new VideoScript
        {
            VideoProjectId = project.Id,
            Title = project.Title,
            Hook = "Trục cao tốc ngang đầu tiên của miền Tây chính thức thành hình!",
            VoiceOverScript = "Tuyến cao tốc nối 4 tỉnh miền Tây Nam Bộ có tổng chiều dài hơn 188 km. Dự án tạo trục hành lang kinh tế kết nối Campuchia ra Biển Đông.",
            KeyFacts = "- Dài 188 km.\n- Nối Châu Đốc đến Cảng Trần Đề.",
            Context = "Miền Tây lâu nay thiếu trục giao thông kết nối theo chiều ngang.",
            Analysis = "Đòn bẩy đột phá thu hút FDI và logistics cho cả vùng ĐBSCL.",
            Conclusion = "Tuyến cao tốc này sẽ làm thay đổi diện mạo kinh tế miền Tây ra sao?",
            SourceAttribution = "Theo Báo Đầu Tư"
        };
        db.VideoScripts.Add(script);

        for (int i = 1; i <= 6; i++)
        {
            db.VideoScenes.Add(new VideoScene
            {
                VideoProjectId = project.Id,
                SceneNumber = i,
                StartTimeSeconds = (i - 1) * 8.0,
                EndTimeSeconds = i * 8.0,
                VoiceText = $"Thuyết minh cảnh #{i}",
                OnScreenText = $"ĐIỂM NHẤN #{i}",
                ScenePurpose = "Fact"
            });
        }
        await db.SaveChangesAsync();

        // Act
        var result = await reviewService.SubmitDecisionAsync(project.Id, new SubmitReviewDecisionRequestDto
        {
            Decision = "Approved",
            ReviewedBy = "Senior Editor",
            FeedbackNotes = "Đạt tiêu chuẩn xuất bản Reels."
        });

        // Assert
        result.Should().NotBeNull();
        result.Decision.Should().Be("Approved");
        result.ReviewedBy.Should().Be("Senior Editor");
        result.FeedbackNotes.Should().Be("Đạt tiêu chuẩn xuất bản Reels.");
        result.CanPublish.Should().BeTrue();
        result.ReviewedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task VideoReviewService_Submits_Rejection_With_Feedback_Notes()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var reviewService = scope.ServiceProvider.GetRequiredService<IVideoReviewService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var project = new VideoProject
        {
            Title = "Dự án bị từ chối duyệt",
            SourceUrl = "https://example.com/reject-test",
            Status = VideoProjectStatus.AwaitingApproval
        };
        db.VideoProjects.Add(project);
        await db.SaveChangesAsync();

        // Act
        var result = await reviewService.SubmitDecisionAsync(project.Id, new SubmitReviewDecisionRequestDto
        {
            Decision = "Rejected",
            ReviewedBy = "Chief Editor",
            FeedbackNotes = "Kịch bản chưa làm rõ tác động môi trường của dự án."
        });

        // Assert
        result.Decision.Should().Be("Rejected");
        result.FeedbackNotes.Should().Be("Kịch bản chưa làm rõ tác động môi trường của dự án.");
        result.CanPublish.Should().BeFalse();
    }

    [Fact]
    public async Task VideoReviewService_Updates_Metadata_Title_Hook_And_Caption()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var reviewService = scope.ServiceProvider.GetRequiredService<IVideoReviewService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var project = new VideoProject
        {
            Title = "Tiêu đề cũ ban đầu",
            SourceUrl = "https://example.com/meta-test",
            Status = VideoProjectStatus.AwaitingApproval
        };
        db.VideoProjects.Add(project);
        await db.SaveChangesAsync();

        var script = new VideoScript
        {
            VideoProjectId = project.Id,
            Title = project.Title,
            Hook = "Hook cũ",
            VoiceOverScript = "Nội dung cũ",
            SourceAttribution = "Báo Test"
        };
        db.VideoScripts.Add(script);

        var scene = new VideoScene
        {
            VideoProjectId = project.Id,
            SceneNumber = 1,
            StartTimeSeconds = 0,
            EndTimeSeconds = 5,
            OnScreenText = "CAPTION CŨ"
        };
        db.VideoScenes.Add(scene);
        await db.SaveChangesAsync();

        // Act
        var updated = await reviewService.UpdateMetadataAsync(project.Id, new UpdateVideoMetadataRequestDto
        {
            Title = "Tiêu đề video mới chuẩn Reels 2026",
            Hook = "Hook mới cực kỳ cuốn hút",
            Caption = "TIÊU ĐIỂM MỚI NHẤT"
        });

        // Assert
        updated.Should().BeTrue();

        using var queryScope = _factory.Services.CreateScope();
        var queryDb = queryScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var reloaded = await queryDb.VideoProjects.FindAsync(project.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Title.Should().Be("Tiêu đề video mới chuẩn Reels 2026");

        var reloadedScript = queryDb.VideoScripts.FirstOrDefault(s => s.VideoProjectId == project.Id);
        reloadedScript!.Title.Should().Be("Tiêu đề video mới chuẩn Reels 2026");
        reloadedScript.Hook.Should().Be("Hook mới cực kỳ cuốn hút");

        var reloadedScene = queryDb.VideoScenes.FirstOrDefault(s => s.VideoProjectId == project.Id && s.SceneNumber == 1);
        reloadedScene!.OnScreenText.Should().Be("TIÊU ĐIỂM MỚI NHẤT");
    }

    [Fact]
    public async Task VideoReviewController_Full_Workflow_Http()
    {
        // 1. Arrange: Create project in DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var article = new Article
        {
            Title = "Hầm chui nút giao Nguyễn Văn Linh thông xe nhánh 1",
            NormalizedTitle = "ham chui nut giao nguyen van linh thong xe nhanh 1",
            Url = $"https://test-news.vn/ham-chui-{Guid.NewGuid()}",
            Publisher = "Báo Sài Gòn Giải Phóng",
            ContentBody = "Sáng nay nhánh hầm HC2 thuộc dự án hầm chui nút giao Nguyễn Văn Linh - Nguyễn Hữu Thọ chính thức thông xe.",
            Status = "Extracted"
        };
        db.Articles.Add(article);
        await db.SaveChangesAsync();

        var project = new VideoProject
        {
            Title = "TIÊU ĐIỂM: Thông xe hầm chui Nguyễn Văn Linh",
            SourceUrl = article.Url,
            Publisher = "Báo SGGP",
            ArticleId = article.Id,
            Status = VideoProjectStatus.AwaitingApproval
        };
        db.VideoProjects.Add(project);
        await db.SaveChangesAsync();

        var script = new VideoScript
        {
            VideoProjectId = project.Id,
            Title = project.Title,
            Hook = "Nhánh hầm chui hơn 800 tỷ đồng tại Nam Sài Gòn đã chính thức thông xe!",
            VoiceOverScript = "Sáng nay nhánh hầm chui tại nút giao Nguyễn Văn Linh - Nguyễn Hữu Thọ chính thức cho xe lưu thông, giải tỏa nút thắt giao thông huyết mạch khu Nam.",
            KeyFacts = "- Vốn đầu tư 830 tỷ.\n- Thông xe nhánh HC2 sáng nay.",
            Context = "Nút giao thường xuyên ùn tắc nghiêm trọng.",
            Analysis = "Giải tỏa áp lực xe container hướng về cảng Hiệp Phước.",
            Conclusion = "Bạn đã đi thử nhánh hầm chui mới này chưa?",
            SourceAttribution = "Theo Báo SGGP"
        };
        db.VideoScripts.Add(script);

        for (int i = 1; i <= 6; i++)
        {
            db.VideoScenes.Add(new VideoScene
            {
                VideoProjectId = project.Id,
                SceneNumber = i,
                StartTimeSeconds = (i - 1) * 8.5,
                EndTimeSeconds = i * 8.5,
                VoiceText = $"Thuyết minh cảnh #{i}",
                OnScreenText = $"HẦM CHUI #{i}"
            });
        }
        await db.SaveChangesAsync();

        // 2. Act: GET /api/video-projects/{id}/review
        var getReviewRes = await _client.GetAsync($"/api/video-projects/{project.Id}/review");
        getReviewRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var reviewDto = await getReviewRes.Content.ReadFromJsonAsync<VideoReviewDto>();
        reviewDto.Should().NotBeNull();
        reviewDto!.VideoProjectId.Should().Be(project.Id);

        // 3. Act: POST /api/video-projects/{id}/review/evaluate
        var evalRes = await _client.PostAsync($"/api/video-projects/{project.Id}/review/evaluate", null);
        evalRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var evalDto = await evalRes.Content.ReadFromJsonAsync<VideoReviewDto>();
        evalDto.Should().NotBeNull();
        evalDto!.Assessment.Should().NotBeNull();
        evalDto.Assessment!.CheckItems.Should().HaveCount(10);

        // 4. Act: POST /api/video-projects/{id}/review/approve
        var approveRes = await _client.PostAsJsonAsync($"/api/video-projects/{project.Id}/review/approve", new SubmitReviewDecisionRequestDto
        {
            Decision = "Approved",
            ReviewedBy = "Reviewer Test",
            FeedbackNotes = "Phê duyệt qua API"
        });
        approveRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Act: PUT /api/video-projects/{id}/review/metadata
        var updateMetaRes = await _client.PutAsJsonAsync($"/api/video-projects/{project.Id}/review/metadata", new UpdateVideoMetadataRequestDto
        {
            Title = "Hầm chui Nguyễn Văn Linh thông xe nhánh đầu tiên",
            Hook = "Nhánh hầm chui hiện đại nhất Nam Sài Gòn đã mở cửa!"
        });
        updateMetaRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Act: DELETE /api/video-projects/{id}
        var deleteRes = await _client.DeleteAsync($"/api/video-projects/{project.Id}");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deleted from DB
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var deletedProject = await verifyDb.VideoProjects.FindAsync(project.Id);
        deletedProject.Should().BeNull();
    }

    [Fact]
    public async Task QualityController_ArticleImageOnly_Detects_External_Images_As_Fatal_Violation()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var qualityController = scope.ServiceProvider.GetRequiredService<IQualityControllerService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Article A (The project's actual source article)
        var articleA = new Article
        {
            Title = "Bài báo gốc chính thức",
            NormalizedTitle = "bai bao goc chinh thuc",
            Url = $"https://test-news.vn/article-a-{Guid.NewGuid()}",
            Publisher = "Báo Tuổi Trẻ",
            ContentBody = "Nội dung bài báo gốc.",
            Status = "Extracted"
        };
        db.Articles.Add(articleA);

        // Article B (An external article / search engine source)
        var articleB = new Article
        {
            Title = "Bài báo bên ngoài Internet",
            NormalizedTitle = "bai bao ben ngoai internet",
            Url = $"https://external-news.vn/article-b-{Guid.NewGuid()}",
            Publisher = "Nguồn ngoài",
            ContentBody = "Ảnh lấy từ bên ngoài không được phép.",
            Status = "Extracted"
        };
        db.Articles.Add(articleB);
        await db.SaveChangesAsync();

        var externalImage = new ArticleImage
        {
            ArticleId = articleB.Id, // Belonging to external article!
            OriginalImageUrl = "https://external-news.vn/forbidden-external.jpg",
            Caption = "Ảnh ngoài bài báo",
            RightsStatus = RightsStatus.Authorized,
            Category = ImageCategory.Panorama
        };
        db.ArticleImages.Add(externalImage);

        var project = new VideoProject
        {
            Title = "Video ArticleImageOnly Vi Phạm Ảnh Ngoài",
            SourceUrl = articleA.Url,
            Publisher = "Báo Tuổi Trẻ",
            ArticleId = articleA.Id,
            Status = VideoProjectStatus.Rendering,
            TargetDurationSeconds = 60,
            ActualDurationSeconds = 58.0
        };
        db.VideoProjects.Add(project);
        await db.SaveChangesAsync();

        // Add 8 scenes, but scene 2 points to external image
        for (int i = 1; i <= 8; i++)
        {
            db.VideoScenes.Add(new VideoScene
            {
                VideoProjectId = project.Id,
                SceneNumber = i,
                StartTimeSeconds = (i - 1) * 7.25,
                EndTimeSeconds = i * 7.25,
                VoiceText = $"Thuyết minh cảnh #{i}",
                OnScreenText = $"TIÊU ĐIỂM #{i}",
                SelectedArticleImageId = i == 2 ? externalImage.Id : null,
                MotionType = "ZoomIn",
                TransitionType = "Fade",
                ScenePurpose = "Fact"
            });
        }
        await db.SaveChangesAsync();

        // Act
        var result = await qualityController.EvaluateProjectQualityAsync(project.Id);

        // Assert: ArticleImageOnly rule blocks external image
        result.Should().NotBeNull();
        result.HasFatalViolations.Should().BeTrue();
        result.CanPublish.Should().BeFalse();
        result.FatalViolations.Should().Contain(v => v.Contains("ArticleImageOnly"));

        var qc5 = result.CheckItems.FirstOrDefault(c => c.CheckCode == "QC_05_IMAGE_RIGHTS");
        qc5.Should().NotBeNull();
        qc5!.Passed.Should().BeFalse();
        qc5.IsFatal.Should().BeTrue();
        qc5.Details.Should().Contain("ArticleImageOnly");
    }
}
