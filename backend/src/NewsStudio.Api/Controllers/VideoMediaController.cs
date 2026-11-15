using Microsoft.AspNetCore.Mvc;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Api.Controllers;

[ApiController]
[Route("api/video-projects/{id:guid}")]
public class VideoMediaController : ControllerBase
{
    private readonly IVideoProductionService _productionService;
    private readonly IVideoAssetService _assetService;
    private readonly ILogger<VideoMediaController> _logger;

    public VideoMediaController(
        IVideoProductionService productionService,
        IVideoAssetService assetService,
        ILogger<VideoMediaController> logger)
    {
        _productionService = productionService;
        _assetService = assetService;
        _logger = logger;
    }

    /// <summary>
    /// Triggers TTS synthesis and vertical 9:16 video rendering.
    /// </summary>
    [HttpPost("render")]
    [ProducesResponseType(typeof(RenderStatusResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TriggerRender(
        Guid id,
        [FromQuery] string? ttsProvider = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _productionService.ProduceVideoAsync(id, ttsProvider, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render video for project {ProjectId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred during video production." });
        }
    }

    /// <summary>
    /// Gets current rendering and production progress.
    /// </summary>
    [HttpGet("render-status")]
    [ProducesResponseType(typeof(RenderStatusResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRenderStatus(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var status = await _productionService.GetProductionStatusAsync(id, cancellationToken);
            return Ok(status);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Streams temporary synthesized voice-over audio for review.
    /// </summary>
    [HttpGet("audio")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StreamAudio(Guid id, CancellationToken cancellationToken)
    {
        var assets = await _assetService.GetAssetsByProjectIdAsync(id, cancellationToken);
        var audioAsset = assets.FirstOrDefault(a => a.AssetType == "Audio" && !a.IsCleanedUp);

        if (audioAsset == null || !System.IO.File.Exists(audioAsset.LocalTempPath))
        {
            return NotFound(new { error = "Audio file not found or has been cleaned up." });
        }

        var stream = new FileStream(audioAsset.LocalTempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, audioAsset.MimeType, enableRangeProcessing: true);
    }

    /// <summary>
    /// Returns synchronized subtitles in SRT or VTT format.
    /// </summary>
    [HttpGet("subtitles")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubtitles(
        Guid id,
        [FromQuery] string format = "vtt",
        CancellationToken cancellationToken = default)
    {
        var assets = await _assetService.GetAssetsByProjectIdAsync(id, cancellationToken);
        var subAsset = assets.FirstOrDefault(a => a.AssetType == "Subtitle" && !a.IsCleanedUp);

        if (subAsset == null || !System.IO.File.Exists(subAsset.LocalTempPath))
        {
            return NotFound(new { error = "Subtitles not found." });
        }

        var subPath = subAsset.LocalTempPath;
        if (format.Equals("vtt", StringComparison.OrdinalIgnoreCase))
        {
            var vttPath = Path.ChangeExtension(subPath, ".vtt");
            if (System.IO.File.Exists(vttPath)) subPath = vttPath;
        }

        var content = await System.IO.File.ReadAllTextAsync(subPath, cancellationToken);
        var contentType = format.Equals("vtt", StringComparison.OrdinalIgnoreCase) ? "text/vtt" : "application/x-subrip";
        return Content(content, contentType, System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// Streams rendered vertical MP4 video with range request support for preview.
    /// </summary>
    [HttpGet("video")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StreamVideo(Guid id, CancellationToken cancellationToken)
    {
        var assets = await _assetService.GetAssetsByProjectIdAsync(id, cancellationToken);
        var videoAsset = assets.FirstOrDefault(a => (a.AssetType == "FinalVideo" || a.AssetType == "Video") && !a.IsCleanedUp);

        if (videoAsset == null || !System.IO.File.Exists(videoAsset.LocalTempPath))
        {
            return NotFound(new { error = "Rendered video file not found or has been cleaned up." });
        }

        var stream = new FileStream(videoAsset.LocalTempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, "video/mp4", enableRangeProcessing: true);
    }

    /// <summary>
    /// Gets list of all temporary ephemeral media assets for this project.
    /// </summary>
    [HttpGet("assets")]
    [ProducesResponseType(typeof(List<VideoAssetDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAssets(Guid id, CancellationToken cancellationToken)
    {
        var assets = await _assetService.GetAssetsByProjectIdAsync(id, cancellationToken);
        return Ok(assets);
    }

    /// <summary>
    /// Manually cleans up temporary assets for this project to free disk space.
    /// </summary>
    [HttpDelete("assets")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CleanupAssets(Guid id, CancellationToken cancellationToken)
    {
        await _assetService.CleanupProjectAssetsAsync(id, cancellationToken);
        return Ok(new { message = $"Temporary assets for project '{id}' have been cleaned up." });
    }
}
