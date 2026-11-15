using Microsoft.EntityFrameworkCore;
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
    private readonly ILogger<VideoProductionService> _logger;

    public VideoProductionService(
        IApplicationDbContext context,
        ITtsFactory ttsFactory,
        IVideoRendererService rendererService,
        IVideoAssetService assetService,
        ILogger<VideoProductionService> logger)
    {
        _context = context;
        _ttsFactory = ttsFactory;
        _rendererService = rendererService;
        _assetService = assetService;
        _logger = logger;
    }

    public async Task<RenderStatusResponseDto> ProduceVideoAsync(
        Guid projectId,
        string? ttsProvider = null,
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

        // 2. Synthesize Speech & Subtitles
        var tts = _ttsFactory.GetTtsService(ttsProvider);
        var ttsResult = await tts.SynthesizeSpeechWithSubtitlesAsync(new TtsSynthesisRequest
        {
            ProjectId = projectId,
            Text = project.Script.VoiceOverScript,
            Voice = "vi-VN-HoaiMyNeural"
        }, cancellationToken);

        // Register Audio and Subtitle Assets
        await _assetService.RegisterAssetAsync(projectId, "Audio", ttsResult.AudioFilePath, "audio/wav", TimeSpan.FromHours(2), cancellationToken);
        await _assetService.RegisterAssetAsync(projectId, "Subtitle", ttsResult.SubtitleFilePath, "application/x-subrip", TimeSpan.FromHours(2), cancellationToken);

        // 3. Advance to Rendering
        project.Status = VideoProjectStatus.Rendering;
        await _context.SaveChangesAsync(cancellationToken);

        // 4. Render 9:16 Vertical Video
        var renderRequest = new VideoRenderRequest
        {
            ProjectId = projectId,
            Title = project.Title,
            AudioFilePath = ttsResult.AudioFilePath,
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
                ImageUrl = s.SelectedArticleImage?.OriginalImageUrl,
                ImageCaption = s.ImageCaption ?? s.SelectedArticleImage?.Caption,
                VisualType = s.SelectedArticleImageId.HasValue ? "Image" : "Typography",
                MotionType = s.MotionType,
                TransitionType = s.TransitionType,
                ScenePurpose = s.ScenePurpose
            }).ToList()
        };

        var renderResult = await _rendererService.RenderVerticalReelAsync(renderRequest, null, cancellationToken);

        // Register Video Asset (Temporary ephemeral MP4)
        await _assetService.RegisterAssetAsync(projectId, "FinalVideo", renderResult.OutputVideoPath, "video/mp4", TimeSpan.FromHours(2), cancellationToken);

        // 5. Update Project record and stop at AwaitingApproval (User MUST approve before publishing)
        project.OutputVideoPath = renderResult.OutputVideoPath;
        project.ActualDurationSeconds = renderResult.DurationSeconds;
        project.Status = VideoProjectStatus.AwaitingApproval;

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
