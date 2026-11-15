using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Domain.Enums;
using NewsStudio.Infrastructure.Ai;
using Xunit;

namespace NewsStudio.Tests;

[Collection("DatabaseCollection")]
public class Phase4AiEditorAndScriptPlannerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public Phase4AiEditorAndScriptPlannerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public void AIEditor_Generates_Script_With_Target_Duration_45_To_60_Seconds()
    {
        // Arrange
        var input = new EditorialScriptPromptInput
        {
            ProjectId = Guid.NewGuid(),
            ArticleTitle = "TP.HCM khánh thành bệnh viện đa khoa hiện đại 1000 giường",
            Publisher = "VnExpress",
            Author = "Lê Phương",
            PublishedDate = DateTime.UtcNow,
            CanonicalUrl = "https://vnexpress.net/khanh-thanh-benh-vien-1000-giuong.html",
            ContentBody = "Sáng 15/11, Bệnh viện Đa khoa mới quy mô 1.000 giường bệnh chính thức khánh thành tại cửa ngõ phía Tây thành phố. Dự án có tổng vốn đầu tư hơn 2.000 tỷ đồng, được trang bị hệ thống máy móc y tế tối tân nhập khẩu từ Đức và Nhật Bản. Công trình kỳ vọng sẽ giảm tải đáng kể cho các bệnh viện tuyến cuối nội đô.",
            TargetDurationSeconds = 55
        };

        // Act: Use the deterministic editorial engine
        var script = GeminiAiEditorService.GenerateDeterministicEditorialScript(input, "Gemini", "gemini-1.5-flash");

        // Assert
        script.EstimatedReadingSeconds.Should().BeInRange(45, 60);
        script.WordCount.Should().BeGreaterThan(50);
        script.VideoTitle.Should().StartWith("TIÊU ĐIỂM:");
        script.Hook.Should().NotBeNullOrWhiteSpace();
        script.VoiceOverScript.Should().NotBeNullOrWhiteSpace();
        script.SourceAttribution.Should().Contain("VnExpress");
    }

    [Fact]
    public void AIEditor_Separates_KeyFacts_And_Commentary()
    {
        // Arrange
        var input = new EditorialScriptPromptInput
        {
            ProjectId = Guid.NewGuid(),
            ArticleTitle = "Tăng trưởng xuất khẩu nông sản đạt kỷ lục 55 tỷ USD",
            Publisher = "Tuổi Trẻ Online",
            PublishedDate = DateTime.UtcNow,
            ContentBody = "Bộ Nông nghiệp và Phát triển nông thôn công bố kim ngạch xuất khẩu nông lâm thủy sản năm 2026 vượt mốc 55 tỷ USD. Thị trường Trung Quốc, Mỹ và EU tiếp tục là ba đối tác nhập khẩu lớn nhất.",
            TargetDurationSeconds = 50
        };

        // Act
        var script = GeminiAiEditorService.GenerateDeterministicEditorialScript(input, "Gemini", "gemini-1.5-flash");

        // Assert
        script.KeyFacts.Should().NotBeNullOrWhiteSpace();
        script.KeyFacts.Should().Contain("Dữ kiện");
        script.Analysis.Should().NotBeNullOrWhiteSpace();
        script.Analysis.Should().NotBe(script.KeyFacts); // Distinct content
        script.Context.Should().NotBeNullOrWhiteSpace();
        script.Conclusion.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ScriptPlanner_Creates_6_To_10_Scenes_Matching_Duration()
    {
        // Arrange
        var planner = new ScriptPlannerService();
        var script = new EditorialScriptResult
        {
            VideoTitle = "TIÊU ĐIỂM: Khánh thành bệnh viện 1000 giường",
            Hook = "Bệnh viện đa khoa 1000 giường hiện đại nhất vừa chính thức đi vào hoạt động!",
            KeyFacts = "- Dữ kiện 1: Vốn đầu tư hơn 2000 tỷ đồng.\n- Dữ kiện 2: Quy mô 1000 giường bệnh.",
            Context = "Công trình nhằm giải quyết áp lực quá tải y tế tại khu vực.",
            Analysis = "Đây là bước tiến chiến lược nâng cao năng lực y tế chuyên sâu cho toàn vùng kinh tế trọng điểm.",
            Conclusion = "Bạn nghĩ sao về công trình y tế hiện đại này? Hãy bình luận bên dưới!",
            SourceAttribution = "Theo VnExpress",
            EstimatedReadingSeconds = 55
        };

        var input = new ScriptPlanningInput
        {
            ProjectId = Guid.NewGuid(),
            Script = script,
            TargetDurationSeconds = 55,
            AvailableImages = new List<ArticleImageInputDto>
            {
                new() { Id = Guid.NewGuid(), OriginalImageUrl = "https://img.vnexpress.net/bv1.jpg", Order = 1, Category = "Panorama", RightsStatus = "Unknown", SelectedForVideo = true },
                new() { Id = Guid.NewGuid(), OriginalImageUrl = "https://img.vnexpress.net/bv2.jpg", Order = 2, Category = "Event", RightsStatus = "Unknown", SelectedForVideo = true }
            }
        };

        // Act
        var scenes = await planner.PlanScenesAsync(input);

        // Assert
        scenes.Count.Should().BeInRange(6, 10);
        
        // Total duration should end in 45-60s
        scenes[^1].EndTimeSeconds.Should().BeInRange(45.0, 60.0);

        // Consecutive time alignment
        for (int i = 0; i < scenes.Count - 1; i++)
        {
            scenes[i + 1].StartTimeSeconds.Should().Be(scenes[i].EndTimeSeconds);
        }

        // Scene purposes should follow narrative flow
        scenes[0].ScenePurpose.Should().Be("Hook");
        scenes[^1].ScenePurpose.Should().Be("Conclusion");
    }

    [Fact]
    public async Task ScriptPlanner_Strictly_Uses_Article_Images_Or_Falls_Back_To_Typography()
    {
        // Arrange
        var planner = new ScriptPlannerService();
        var image1Id = Guid.NewGuid();
        var image2Id = Guid.NewGuid();

        var script = new EditorialScriptResult
        {
            VideoTitle = "Dự án Năng lượng xanh",
            Hook = "Đại dự án năng lượng tái tạo vừa đóng điện thành công!",
            KeyFacts = "- Công suất 500MW.\n- Sản lượng 1 tỷ kWh mỗi năm.",
            Context = "Góp phần giảm phát thải ròng carbon.",
            Analysis = "Bước ngoặt cho hạ tầng năng lượng sạch.",
            Conclusion = "Chia sẻ ý kiến của bạn về năng lượng tái tạo!",
            SourceAttribution = "Theo Dân Trí",
            EstimatedReadingSeconds = 50
        };

        var availableImages = new List<ArticleImageInputDto>
        {
            new() { Id = image1Id, OriginalImageUrl = "https://img.dantri.com.vn/img1.jpg", Order = 1, Category = "Panorama", RightsStatus = "Unknown", SelectedForVideo = true },
            new() { Id = image2Id, OriginalImageUrl = "https://img.dantri.com.vn/img2.jpg", Order = 2, Category = "Event", RightsStatus = "Unknown", SelectedForVideo = true }
        };

        var input = new ScriptPlanningInput
        {
            ProjectId = Guid.NewGuid(),
            Script = script,
            AvailableImages = availableImages,
            TargetDurationSeconds = 50
        };

        // Act
        var scenes = await planner.PlanScenesAsync(input);

        // Assert
        scenes.Count.Should().BeInRange(6, 10);

        // First 2 scenes get the only 2 available article images
        scenes[0].SelectedArticleImageId.Should().Be(image1Id);
        scenes[0].VisualType.Should().Be("Image");

        scenes[1].SelectedArticleImageId.Should().Be(image2Id);
        scenes[1].VisualType.Should().Be("Image");

        // Remaining scenes (3 to N) MUST fallback to Typography / MetricChart (No fake images!)
        for (int i = 2; i < scenes.Count; i++)
        {
            scenes[i].SelectedArticleImageId.Should().BeNull();
            scenes[i].VisualType.Should().BeOneOf("Typography", "MetricChart", "Timeline");
            scenes[i].OnScreenText.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task ScriptPlanner_When_Article_Has_Zero_Images_All_Scenes_Use_Typography()
    {
        // Arrange
        var planner = new ScriptPlannerService();
        var script = new EditorialScriptResult
        {
            VideoTitle = "Bản tin không hình ảnh",
            Hook = "Thông báo quan trọng vừa được ban hành sáng nay!",
            KeyFacts = "Thông tin quyết định điều chỉnh lịch trình.",
            Context = "Áp dụng toàn diện.",
            Analysis = "Đảm bảo tiến độ công việc.",
            Conclusion = "Theo dõi để biết thêm chi tiết.",
            SourceAttribution = "Theo Báo điện tử",
            EstimatedReadingSeconds = 48
        };

        var input = new ScriptPlanningInput
        {
            ProjectId = Guid.NewGuid(),
            Script = script,
            AvailableImages = new List<ArticleImageInputDto>(), // Zero images
            TargetDurationSeconds = 50
        };

        // Act
        var scenes = await planner.PlanScenesAsync(input);

        // Assert
        scenes.Count.Should().BeInRange(6, 10);
        scenes.All(s => s.SelectedArticleImageId == null).Should().BeTrue();
        scenes.All(s => s.VisualType == "Typography" || s.VisualType == "MetricChart").Should().BeTrue();
    }

    [Fact]
    public void AiEditorFactory_Switches_Between_Gemini_And_Claude()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IAiEditorFactory>();

        // Act
        var gemini = factory.GetEditorService("Gemini");
        var claude = factory.GetEditorService("Claude");
        var defaultService = factory.GetEditorService((string?)null);

        // Assert
        gemini.ProviderType.Should().Be(AiProviderType.Gemini);
        claude.ProviderType.Should().Be(AiProviderType.Claude);
        defaultService.ProviderType.Should().Be(AiProviderType.Gemini);
    }

    [Fact]
    public async Task VideoProjectScriptsController_Full_Workflow()
    {
        // 1. Arrange: Create test project with article in DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var article = new Article
        {
            Title = "TP.HCM phát triển trung tâm tài chính quốc tế",
            NormalizedTitle = "tp hcm phat trien trung tam tai chinh quoc te",
            Url = $"https://test-news.vn/finance-{Guid.NewGuid()}",
            Publisher = "Thời Báo Tài Chính",
            ContentBody = "Đề án xây dựng trung tâm tài chính quốc tế tại TP.HCM vừa được trình Thủ tướng Chính phủ. Dự án quy hoạch tại Khu đô thị mới Thủ Thiêm với các cơ chế thử nghiệm sandbox đặc thù.",
            Status = "Extracted"
        };
        db.Articles.Add(article);
        await db.SaveChangesAsync();

        var img = new ArticleImage
        {
            ArticleId = article.Id,
            OriginalImageUrl = "https://test-news.vn/finance-thu-thiem.jpg",
            Caption = "Khu đô thị mới Thủ Thiêm",
            ArticleImageOrder = 1,
            RightsStatus = RightsStatus.Unknown,
            Category = ImageCategory.Panorama,
            Status = "Extracted"
        };
        db.ArticleImages.Add(img);

        var project = new VideoProject
        {
            Title = "Dự án Tài chính Thủ Thiêm",
            SourceUrl = article.Url,
            Publisher = "Thời Báo Tài Chính",
            ArticleId = article.Id,
            Status = VideoProjectStatus.Extracted,
            TargetDurationSeconds = 55
        };
        db.VideoProjects.Add(project);
        await db.SaveChangesAsync();

        // 2. Act: Generate AI Script via POST /api/video-projects/{id}/script/generate
        var genRequest = new GenerateScriptRequestDto { AiProvider = "Gemini", TargetDurationSeconds = 55 };
        var genResponse = await _client.PostAsJsonAsync($"/api/video-projects/{project.Id}/script/generate", genRequest);

        genResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var genScript = await genResponse.Content.ReadFromJsonAsync<VideoScriptResponseDto>();
        genScript.Should().NotBeNull();
        genScript!.VideoProjectId.Should().Be(project.Id);
        genScript.Title.Should().NotBeNullOrWhiteSpace();
        genScript.Hook.Should().NotBeNullOrWhiteSpace();
        genScript.VoiceOverScript.Should().NotBeNullOrWhiteSpace();
        genScript.KeyFacts.Should().NotBeNullOrWhiteSpace();
        genScript.Analysis.Should().NotBeNullOrWhiteSpace();
        genScript.EstimatedReadingSeconds.Should().BeInRange(45, 60);

        // 3. Act: Query script via GET /api/video-projects/{id}/script
        var getScriptResponse = await _client.GetAsync($"/api/video-projects/{project.Id}/script");
        getScriptResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetchedScript = await getScriptResponse.Content.ReadFromJsonAsync<VideoScriptResponseDto>();
        fetchedScript.Should().NotBeNull();
        fetchedScript!.Id.Should().Be(genScript.Id);

        // 4. Act: Update script manually via PUT /api/video-projects/{id}/script
        var updateRequest = new UpdateScriptRequestDto
        {
            Title = "BIÊN TẬP: Trung tâm Tài chính Quốc tế Thủ Thiêm",
            Hook = "Thủ Thiêm sắp trở thành trung tâm tài chính tầm cỡ thế giới?",
            VoiceOverScript = "Kế hoạch xây dựng trung tâm tài chính quốc tế tại Thủ Thiêm chính thức được đệ trình với kỳ vọng thu hút dòng vốn toàn cầu. Hãy theo dõi để biết chi tiết.",
            KeyFacts = "- Quy hoạch tại Thủ Thiêm.\n- Cơ chế sandbox đặc thù.",
            Context = "Phục vụ chiến lược phát triển kinh tế vùng.",
            Analysis = "Đây là đòn bẩy tài chính quan trọng trong thập kỷ tới.",
            Conclusion = "Bạn kỳ vọng gì về trung tâm này?",
            SourceAttribution = "Theo Thời Báo Tài Chính"
        };
        var updateResponse = await _client.PutAsJsonAsync($"/api/video-projects/{project.Id}/script", updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedScript = await updateResponse.Content.ReadFromJsonAsync<VideoScriptResponseDto>();
        updatedScript!.Title.Should().Be("BIÊN TẬP: Trung tâm Tài chính Quốc tế Thủ Thiêm");

        // 5. Act: Query scenes via GET /api/video-projects/{id}/scenes
        var getScenesResponse = await _client.GetAsync($"/api/video-projects/{project.Id}/scenes");
        getScenesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var scenes = await getScenesResponse.Content.ReadFromJsonAsync<List<VideoScenePlanDto>>();
        scenes.Should().NotBeNull();
        scenes!.Count.Should().BeInRange(6, 10);
        scenes![0].SelectedArticleImageId.Should().Be(img.Id); // Uses the article image
    }
}
