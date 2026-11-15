using Microsoft.AspNetCore.Mvc;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Api.Controllers;

[ApiController]
[Route("api/video-projects/{id:guid}")]
[Produces("application/json")]
public class VideoProjectScriptsController : ControllerBase
{
    private readonly IScriptService _scriptService;
    private readonly ILogger<VideoProjectScriptsController> _logger;

    public VideoProjectScriptsController(
        IScriptService scriptService,
        ILogger<VideoProjectScriptsController> logger)
    {
        _scriptService = scriptService;
        _logger = logger;
    }

    /// <summary>
    /// Generates an AI editorial script (45-60s) from the project's linked article using Gemini or Claude.
    /// </summary>
    [HttpPost("script/generate")]
    [ProducesResponseType(typeof(VideoScriptResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateScript(
        Guid id,
        [FromBody] GenerateScriptRequestDto? options,
        CancellationToken cancellationToken)
    {
        try
        {
            var script = await _scriptService.GenerateScriptForProjectAsync(id, options, cancellationToken);
            return Ok(script);
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
            _logger.LogError(ex, "Failed to generate editorial script for project {ProjectId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while generating script." });
        }
    }

    /// <summary>
    /// Gets the existing editorial script for a video project.
    /// </summary>
    [HttpGet("script")]
    [ProducesResponseType(typeof(VideoScriptResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetScript(Guid id, CancellationToken cancellationToken)
    {
        var script = await _scriptService.GetScriptByProjectIdAsync(id, cancellationToken);
        if (script == null)
        {
            return NotFound(new { error = $"Script for project '{id}' has not been generated yet." });
        }

        return Ok(script);
    }

    /// <summary>
    /// Updates the editorial script content (Title, Hook, VoiceOver, KeyFacts, Context, Analysis, Conclusion).
    /// </summary>
    [HttpPut("script")]
    [ProducesResponseType(typeof(VideoScriptResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateScript(
        Guid id,
        [FromBody] UpdateScriptRequestDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _scriptService.UpdateScriptAsync(id, dto, cancellationToken);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Plans 6-10 video scenes from the editorial script, strictly matching original article images.
    /// </summary>
    [HttpPost("scenes/plan")]
    [ProducesResponseType(typeof(List<VideoScenePlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PlanScenes(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var scenes = await _scriptService.PlanScenesForProjectAsync(id, cancellationToken);
            return Ok(scenes);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets the planned scenes for a video project.
    /// </summary>
    [HttpGet("scenes")]
    [ProducesResponseType(typeof(List<VideoScenePlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScenes(Guid id, CancellationToken cancellationToken)
    {
        var scenes = await _scriptService.GetScenesByProjectIdAsync(id, cancellationToken);
        return Ok(scenes);
    }

    /// <summary>
    /// Updates the video scenes (text, on-screen text, image assignments, motion, transition).
    /// </summary>
    [HttpPut("scenes")]
    [ProducesResponseType(typeof(List<VideoScenePlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateScenes(
        Guid id,
        [FromBody] UpdateScenesRequestDto dto,
        CancellationToken cancellationToken)
    {
        var scenes = await _scriptService.UpdateScenesAsync(id, dto, cancellationToken);
        return Ok(scenes);
    }
}
