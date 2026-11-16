using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Infrastructure.Rendering;

public class VideoProductionService : IVideoProductionService
{
    private readonly IApplicationDbContext _context;
    private readonly ITtsFactory _ttsFactory;
    private readonly IVideoRendererService _rendererService;
    private readonly IVideoAssetService _assetService;
    private readonly SceneImageService _sceneImages;
    private readonly ILogger<VideoProductionService> _logger;
    private readonly TimeSpan _assetTtl;
    private readonly IConfiguration _configuration;

    public VideoProductionService(
        IApplicationDbContext context,
        ITtsFactory ttsFactory,
        IVideoRendererService rendererService,
        IVideoAssetService assetService,
        SceneImageService sceneImages,
        IConfiguration configuration,
        ILogger<VideoProductionService> logger)
    {
        _sceneImages = sceneImages;
        _context = context;
        _ttsFactory = ttsFactory;
        _rendererService = rendererService;
        _assetService = assetService;
        _logger = logger;
        _configuration = configuration;
        // Same setting the System page reports, so what users see is what actually happens
        _assetTtl = TimeSpan.FromMinutes(Math.Max(5, configuration.GetValue<int>("Storage:TempFileTtlMinutes", 120)));
    }

    public async Task<RenderStatusResponseDto> ProduceVideoAsync(
        Guid projectId,
        string? ttsProvider = null,
        string? voice = null,
        string? speed = null,
        CancellationToken cancellationToken = default)
    {
        var project = await _context.VideoProjects
            .Include(p => p.Script)
            .Include(p => p.Scenes)
                .ThenInclude(s => s.SelectedArticleImage)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            throw new KeyNotFoundException($"VideoProject '{projectId}' not found.");
        }

        if (project.Script == null)
        {
            throw new InvalidOperationException($"Project '{projectId}' has no script generated.");
        }

        _logger.LogInformation("Starting VideoProduction pipeline for Project {ProjectId} ('{Title}')",
            projectId, project.Title);

        // 1. Advance to GeneratingAudio
        project.Status = VideoProjectStatus.GeneratingAudio;
        await _context.SaveChangesAsync(cancellationToken);

        VideoRenderResult renderResult;
        try
        {
            // 1b. Real pictures for every scene (download + related-image fallback) — checked before
            //     the paid voice-over so a video with no usable image fails early and cheaply.
            await _sceneImages.EnsureSceneImagesAsync(project, cancellationToken);

            // 2. Synthesize Speech & Subtitles
            // Explicit request > choice saved on the project > configured default
            var selectedVoice = !string.IsNullOrWhiteSpace(voice)
                ? voice
                : project.Voice ?? _configuration["Tts:DefaultVoice"] ?? "gemini:Kore";
            var selectedSpeed = !string.IsNullOrWhiteSpace(speed) ? speed : project.VoiceSpeed ?? "+0%";
            // The voice id decides the engine: "gemini:*" → Gemini TTS, otherwise Edge-TTS.
            var tts = GeminiTtsService.IsGeminiVoice(selectedVoice)
                ? _ttsFactory.GetTtsService("Gemini")
                : _ttsFactory.GetTtsService(ttsProvider);

            var ttsResult = await tts.SynthesizeSpeechWithSubtitlesAsync(new TtsSynthesisRequest
            {
                ProjectId = projectId,
                Text = project.Script.VoiceOverScript,
                Voice = selectedVoice,
                Speed = selectedSpeed
            }, cancellationToken);

            // Register Audio and Subtitle Assets
            await _assetService.RegisterAssetAsync(projectId, "Audio", ttsResult.AudioFilePath, "audio/wav", _assetTtl, cancellationToken);
            await _assetService.RegisterAssetAsync(projectId, "Subtitle", ttsResult.SubtitleFilePath, "application/x-subrip", _assetTtl, cancellationToken);

            // 3. Fit scene timings to the real narration length, so pictures change with the
            //    words and the video ends when the voice ends (no silent tail).
            FitScenesToAudio(project.Scenes.OrderBy(s => s.SceneNumber).ToList(), ttsResult.AudioDurationSeconds);

            project.Status = VideoProjectStatus.Rendering;
            await _context.SaveChangesAsync(cancellationToken);

            // 4. Render 9:16 Vertical Video
            var renderRequest = new VideoRenderRequest
            {
                ProjectId = projectId,
                Title = project.Title,
                Summary = project.Script.Hook,
                Publisher =!string.IsNullOrWhiteSpace(project.Publisher) ? project.Publisher : "TIN TỨC 24H",
                PublishedDate = project.CreatedAtUtc,
                AudioFilePath = ttsResult.AudioFilePath,
                AudioDurationSeconds = ttsResult.AudioDurationSeconds,
                SubtitleFilePath = ttsResult.SubtitleFilePath,
                Resolution = project.Resolution,
                AspectRatio = project.AspectRatio,
                Fps = project.Fps,
                Scenes = project.Scenes.OrderBy(s => s.SceneNumber).Select(s => new VideoScenePlanDto
                {
                    SceneNumber = s.SceneNumber,
                    StartTimeSeconds = s.StartTimeSeconds,
                    EndTimeSeconds = s.EndTimeSeconds,
                    VoiceText = s.VoiceText,
                    OnScreenText = s.OnScreenText,
                    SelectedArticleImageId = s.SelectedArticleImageId,
                    // Prefer the copy already downloaded and verified by SceneImageService
                    ImageUrl = !string.IsNullOrWhiteSpace(s.SelectedArticleImage?.LocalTempPath) && File.Exists(s.SelectedArticleImage.LocalTempPath)
                        ? s.SelectedArticleImage.LocalTempPath
                        : s.SelectedArticleImage?.OriginalImageUrl,
                    ImageCaption = s.ImageCaption ?? s.SelectedArticleImage?.Caption,
                    VisualType = s.SelectedArticleImageId.HasValue ? "Image" : "Typography",
                    MotionType = s.MotionType,
                    TransitionType = s.TransitionType,
                    ScenePurpose = s.ScenePurpose
                }).ToList()
            };

            renderResult = await _rendererService.RenderVerticalReelAsync(renderRequest, null, cancellationToken);

            // Register Video Asset (Temporary ephemeral MP4)
            await _assetService.RegisterAssetAsync(projectId, "FinalVideo", renderResult.OutputVideoPath, "video/mp4", _assetTtl, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Don't leave the project stuck in GeneratingAudio/Rendering — surface the real reason.
            _logger.LogError(ex, "Video production failed for Project {ProjectId}", projectId);
            project.Status = VideoProjectStatus.Failed;
            project.ErrorMessage = ex.Message;
            await _context.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        // 5. Update Project record and stop at AwaitingApproval (User MUST approve before publishing)
        project.OutputVideoPath = renderResult.OutputVideoPath;
        project.ActualDurationSeconds = renderResult.DurationSeconds;
        project.Status = VideoProjectStatus.AwaitingApproval;
        project.ErrorMessage = null;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("VideoProduction pipeline completed for Project {ProjectId}. Status transitioned to AwaitingApproval.", projectId);

        return new RenderStatusResponseDto
        {
            ProjectId = projectId,
            Status = project.Status.ToString(),
            ProgressPercentage = 100,
            DurationSeconds = renderResult.DurationSeconds,
            VideoUrl = $"/api/video-projects/{projectId}/video",
            AudioUrl = $"/api/video-projects/{projectId}/audio",
            SubtitleUrl = $"/api/video-projects/{projectId}/subtitles"
        };
    }

    /// <summary>
    /// Re-times scenes so they cover exactly the narration: each scene gets a share of the audio
    /// proportional to the number of words it narrates (minimum 1.5 s).
    /// </summary>
    private static void FitScenesToAudio(List<NewsStudio.Domain.Entities.VideoScene> scenes, double audioSeconds)
    {
        if (scenes.Count == 0 || audioSeconds <= 0) return;

        static int Words(string? t) => string.IsNullOrWhiteSpace(t)
            ? 1
            : Math.Max(1, t.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length);

        var weights = scenes.Select(s => (double)Words(s.VoiceText)).ToList();
        var total = weights.Sum();
        double start = 0;
        for (var i = 0; i < scenes.Count; i++)
        {
            var end = i == scenes.Count - 1
                ? audioSeconds
                : Math.Min(audioSeconds, start + Math.Max(1.5, audioSeconds * weights[i] / total));
            scenes[i].StartTimeSeconds = Math.Round(start, 2);
            scenes[i].EndTimeSeconds = Math.Round(end, 2);
            start = end;
        }
    }

    public async Task<RenderStatusResponseDto> GetProductionStatusAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _context.VideoProjects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            throw new KeyNotFoundException($"VideoProject '{projectId}' not found.");
        }

        int progress = project.Status switch
        {
            VideoProjectStatus.GeneratingAudio => 85,
            VideoProjectStatus.Rendering => 95,
            VideoProjectStatus.QualityChecking or VideoProjectStatus.AwaitingApproval => 100,
            VideoProjectStatus.Published => 100,
            _ => 50
        };

        return new RenderStatusResponseDto
        {
            ProjectId = projectId,
            Status = project.Status.ToString(),
            ProgressPercentage = progress,
            DurationSeconds = project.ActualDurationSeconds,
            VideoUrl = !string.IsNullOrEmpty(project.OutputVideoPath) ? $"/api/video-projects/{projectId}/video" : null,
            AudioUrl = $"/api/video-projects/{projectId}/audio",
            SubtitleUrl = $"/api/video-projects/{projectId}/subtitles",
            ErrorMessage = project.ErrorMessage
        };
    }
}
