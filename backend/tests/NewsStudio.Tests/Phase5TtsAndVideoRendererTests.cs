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
using NewsStudio.Infrastructure.Rendering;
using Xunit;

namespace NewsStudio.Tests;

[Collection("DatabaseCollection")]
public class Phase5TtsAndVideoRendererTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public Phase5TtsAndVideoRendererTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task TtsService_Generates_Vietnamese_Speech_Audio_And_Subtitles()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var tts = scope.ServiceProvider.GetRequiredService<ITtsFactory>().GetTtsService(TtsProviderType.EdgeTts);

        var request = new TtsSynthesisRequest
        {
            ProjectId = Guid.NewGuid(),
            Text = "Tuyến metro số 1 Bến Thành - Suối Tiên chính thức vận hành thương mại hôm nay. Hàng nghìn người dân TP.HCM đã hào hứng tham gia trải nghiệm trong ngày đầu mở cửa. Toàn tuyến có 14 ga hiện đại kết nối từ trung tâm đến cửa ngõ thành phố. Hành khách được miễn phí vé trong tháng đầu tiên để làm quen với hình thức giao thông xanh văn minh này.",
            Voice = "vi-VN-HoaiMyNeural"
        };

        // Act
        var result = await tts.SynthesizeSpeechWithSubtitlesAsync(request);

        // Assert
        result.Should().NotBeNull();
        File.Exists(result.AudioFilePath).Should().BeTrue();
        result.FileSizeBytes.Should().BeGreaterThan(1000);
        result.AudioDurationSeconds.Should().BeInRange(55.0, 65.0);
        result.Sha256Hash.Should().NotBeNullOrWhiteSpace();

        // Subtitles verification
        File.Exists(result.SubtitleFilePath).Should().BeTrue();
        File.Exists(result.SubtitleVttFilePath).Should().BeTrue();
        result.Subtitles.Should().NotBeEmpty();
        result.Subtitles[0].StartTimeSeconds.Should().Be(0);
        result.Subtitles[^1].EndTimeSeconds.Should().Be(result.AudioDurationSeconds);
    }

    [Fact]
    public async Task SubtitleGeneratorService_Generates_Valid_Srt_And_Vtt_Format()
    {
        // Arrange
        var generator = new SubtitleGeneratorService();
        var tempSrt = Path.Combine(Path.GetTempPath(), $"sub_{Guid.NewGuid()}.srt");
        var tempVtt = Path.Combine(Path.GetTempPath(), $"sub_{Guid.NewGuid()}.vtt");

        var cues = new List<SubtitleCueDto>
        {
            new() { Index = 1, StartTimeSeconds = 0.0, EndTimeSeconds = 5.2, Text = "METRO SỐ 1 CHÍNH THỨC VẬN HÀNH" },
            new() { Index = 2, StartTimeSeconds = 5.2, EndTimeSeconds = 11.8, Text = "Chiều dài 20 km với 14 nhà ga hiện đại." }
        };

        // Act
        await generator.GenerateSrtAsync(cues, tempSrt);
        await generator.GenerateVttAsync(cues, tempVtt);

        // Assert SRT
        var srtText = await File.ReadAllTextAsync(tempSrt);
        srtText.Should().Contain("1\r\n00:00:00,000 --> 00:00:05,200\r\nMETRO SỐ 1");
        srtText.Should().Contain("2\r\n00:00:05,200 --> 00:00:11,800");

        // Assert VTT
        var vttText = await File.ReadAllTextAsync(tempVtt);
        vttText.Should().StartWith("WEBVTT");
        vttText.Should().Contain("00:00:00.000 --> 00:00:05.200");

        // Cleanup
        if (File.Exists(tempSrt)) File.Delete(tempSrt);
        if (File.Exists(tempVtt)) File.Delete(tempVtt);
    }

    [Fact]
    public async Task FFmpegRendererService_Builds_9x16_Vertical_Reel_Output()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var renderer = scope.ServiceProvider.GetRequiredService<IVideoRendererService>();

        var request = new VideoRenderRequest
        {
            ProjectId = Guid.NewGuid(),
            Title = "Tin tức Metro 9:16",
            Resolution = "1080x1920",
            AspectRatio = "9:16",
            Fps = 30,
            Scenes = new List<VideoScenePlanDto>
            {
                new() { SceneNumber = 1, StartTimeSeconds = 0, EndTimeSeconds = 6, VoiceText = "Mở đầu bản tin", OnScreenText = "TIN NÓNG 24H", VisualType = "Typography" },
                new() { SceneNumber = 2, StartTimeSeconds = 6, EndTimeSeconds = 50, VoiceText = "Nội dung chính", OnScreenText = "METRO THÀNH PHỐ", VisualType = "Typography" }
            }
        };

        // Act
        var result = await renderer.RenderVerticalReelAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Resolution.Should().Be("1080x1920");
        result.DurationSeconds.Should().BeInRange(55.0, 65.0);
        result.FileSizeBytes.Should().BeGreaterThan(1000);
        result.MimeType.Should().Be("video/mp4");
        File.Exists(result.OutputVideoPath).Should().BeTrue();
    }

    [Fact]
    public async Task VideoAssetService_Registers_And_Enforces_Ephemeral_Storage()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var assetService = scope.ServiceProvider.GetRequiredService<IVideoAssetService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var project = new VideoProject
        {
            Title = "Test Project Ephemeral Storage",
            SourceUrl = "https://example.com/news/123",
            Status = VideoProjectStatus.Pending
        };
        db.VideoProjects.Add(project);
        await db.SaveChangesAsync();

        var projectId = project.Id;
        var dummyPath = Path.Combine(Path.GetTempPath(), $"test_video_{Guid.NewGuid()}.mp4");
        await File.WriteAllBytesAsync(dummyPath, new byte[] { 0x01, 0x02, 0x03, 0x04 });

        // Act
        var assetDto = await assetService.RegisterAssetAsync(
            projectId,
            "FinalVideo",
            dummyPath,
            "video/mp4",
            TimeSpan.FromHours(2));

        // Assert
        assetDto.Should().NotBeNull();
        assetDto.FileSizeBytes.Should().Be(4);
        assetDto.MimeType.Should().Be("video/mp4");
        assetDto.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow.AddMinutes(90));

        // Verify in PostgreSQL: ONLY metadata stored, NO binary MP4 column
        var savedAsset = await db.VideoAssets.FindAsync(assetDto.Id);
        savedAsset.Should().NotBeNull();
        savedAsset!.LocalTempPath.Should().Be(dummyPath);
        savedAsset.FileSizeBytes.Should().Be(4);

        // Cleanup
        if (File.Exists(dummyPath)) File.Delete(dummyPath);
    }

    [Fact]
    public async Task VideoAssetService_Cleans_Up_Expired_Assets()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var assetService = scope.ServiceProvider.GetRequiredService<IVideoAssetService>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var project = new VideoProject
        {
            Title = "Test Project Expired Asset Cleanup",
            SourceUrl = "https://example.com/news/456",
            Status = VideoProjectStatus.Pending
        };
        db.VideoProjects.Add(project);
        await db.SaveChangesAsync();

        var projectId = project.Id;
        var tempFile = Path.Combine(Path.GetTempPath(), $"expired_test_{Guid.NewGuid()}.tmp");
        await File.WriteAllTextAsync(tempFile, "Temporary ephemeral content");

        var expiredAsset = new VideoAsset
        {
            VideoProjectId = projectId,
            AssetType = "Audio",
            LocalTempPath = tempFile,
            FileSizeBytes = 25,
            MimeType = "audio/wav",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(-1), // Already expired 1 hour ago
            IsCleanedUp = false,
            Status = "Active"
        };
        db.VideoAssets.Add(expiredAsset);
        await db.SaveChangesAsync();

        // Act
        var cleanedCount = await assetService.CleanupExpiredAssetsAsync();

        // Assert
        cleanedCount.Should().BeGreaterThanOrEqualTo(1);
        File.Exists(tempFile).Should().BeFalse(); // File removed from disk

        using var queryScope = _factory.Services.CreateScope();
        var queryDb = queryScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var reloaded = await queryDb.VideoAssets.FindAsync(expiredAsset.Id);
        reloaded!.IsCleanedUp.Should().BeTrue();
        reloaded.Status.Should().Be("CleanedUp");
    }

    [Fact]
    public void TtsFactory_Switches_Between_EdgeTts_And_ElevenLabs()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<ITtsFactory>();

        // Act
        var edge = factory.GetTtsService("EdgeTts");
        var eleven = factory.GetTtsService("ElevenLabs");
        var defaultTts = factory.GetTtsService((string?)null);

        // Assert
        edge.ProviderType.Should().Be(TtsProviderType.EdgeTts);
        eleven.ProviderType.Should().Be(TtsProviderType.ElevenLabs);
        defaultTts.ProviderType.Should().Be(TtsProviderType.EdgeTts);
    }

    [Fact]
    public async Task VideoMediaController_Endpoints_Full_Workflow()
    {
        // 1. Arrange: Create project, article, script and scenes in DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var article = new Article
        {
            Title = "TP.HCM khởi công cầu Thủ Thiêm 4",
            NormalizedTitle = "tp hcm khoi cong cau thu thiem 4",
            Url = $"https://test-news.vn/cau-thu-thiem-4-{Guid.NewGuid()}",
            Publisher = "Báo Giao Thông",
            ContentBody = "Sáng 15/11, dự án cầu Thủ Thiêm 4 nối Quận 7 và TP.Thủ Đức chính thức khởi công với tổng vốn đầu tư hơn 6.000 tỷ đồng.",
            Status = "Extracted"
        };
        db.Articles.Add(article);
        await db.SaveChangesAsync();

        var project = new VideoProject
        {
            Title = "TIÊU ĐIỂM: Khởi công Cầu Thủ Thiêm 4",
            SourceUrl = article.Url,
            Publisher = "Báo Giao Thông",
            ArticleId = article.Id,
            Status = VideoProjectStatus.PlanningScenes,
            TargetDurationSeconds = 55
        };
        db.VideoProjects.Add(project);
        await db.SaveChangesAsync();

        var script = new VideoScript
        {
            VideoProjectId = project.Id,
            Title = project.Title,
            Hook = "Cầu Thủ Thiêm 4 hơn 6.000 tỷ đồng chính thức khởi công sáng nay!",
            VoiceOverScript = "Sáng 15/11, dự án cầu Thủ Thiêm 4 nối Quận 7 và TP.Thủ Đức chính thức khởi công với tổng vốn đầu tư hơn 6.000 tỷ đồng. Cây cầu có chiều dài hơn 2 km với thiết kế nhịp nâng độc đáo lần đầu tiên xuất hiện tại Việt Nam. Công trình dự kiến hoàn thành sau 36 tháng thi công, giúp giải tỏa áp lực giao thông khu vực Nam Sài Gòn.",
            KeyFacts = "- Vốn đầu tư 6000 tỷ đồng.\n- Nối Quận 7 và TP.Thủ Đức.",
            Context = "Giảm ùn tắc cho khu vực phía Nam.",
            Analysis = "Đột phá về hạ tầng giao thông đô thị.",
            Conclusion = "Bạn đánh giá thế nào về thiết kế cây cầu này?",
            SourceAttribution = "Theo Báo Giao Thông"
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
                VoiceText = $"Đoạn thuyết minh phân cảnh số {i}",
                OnScreenText = $"TIÊU ĐIỂM #{i}",
                MotionType = "ZoomIn",
                TransitionType = "Fade",
                ScenePurpose = "Fact"
            });
        }
        await db.SaveChangesAsync();

        // 2. Act: Trigger Render via POST /api/video-projects/{id}/render
        var renderResponse = await _client.PostAsync($"/api/video-projects/{project.Id}/render", null);
        renderResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var renderStatus = await renderResponse.Content.ReadFromJsonAsync<RenderStatusResponseDto>();
        renderStatus.Should().NotBeNull();
        renderStatus!.Status.Should().Be("AwaitingApproval");
        renderStatus.ProgressPercentage.Should().Be(100);

        // 3. Act: Check render status via GET /api/video-projects/{id}/render-status
        var statusResponse = await _client.GetAsync($"/api/video-projects/{project.Id}/render-status");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Act: Stream Audio via GET /api/video-projects/{id}/audio
        var audioResponse = await _client.GetAsync($"/api/video-projects/{project.Id}/audio");
        audioResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        audioResponse.Content.Headers.ContentType!.MediaType.Should().Be("audio/wav");

        // 5. Act: Get Subtitles via GET /api/video-projects/{id}/subtitles?format=srt
        var subResponse = await _client.GetAsync($"/api/video-projects/{project.Id}/subtitles?format=srt");
        subResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var subContent = await subResponse.Content.ReadAsStringAsync();
        subContent.Should().Contain("-->");

        // 6. Act: Stream Video via GET /api/video-projects/{id}/video
        var videoResponse = await _client.GetAsync($"/api/video-projects/{project.Id}/video");
        videoResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        videoResponse.Content.Headers.ContentType!.MediaType.Should().Be("video/mp4");

        // 7. Act: Get Assets via GET /api/video-projects/{id}/assets
        var assetsResponse = await _client.GetAsync($"/api/video-projects/{project.Id}/assets");
        assetsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var assetsList = await assetsResponse.Content.ReadFromJsonAsync<List<VideoAssetDto>>();
        assetsList.Should().NotBeNull();
        assetsList!.Count.Should().BeGreaterThanOrEqualTo(3); // Audio, Subtitle, FinalVideo

        // 8. Act: Cleanup Assets via DELETE /api/video-projects/{id}/assets
        var cleanupResponse = await _client.DeleteAsync($"/api/video-projects/{project.Id}/assets");
        cleanupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
