using Microsoft.AspNetCore.Mvc;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Infrastructure.Services;

namespace NewsStudio.Api.Controllers;

/// <summary>
/// "Tự động đăng" — every connected Facebook Page has its own topic and daily schedule; the hottest stories
/// of that topic are turned into videos and posted to the page with its own token.
/// </summary>
[ApiController]
[Route("api/auto-post")]
[Produces("application/json")]
public class AutoPostController : ControllerBase
{
    private readonly IAutoPostService _autoPost;
    private readonly ILogger<AutoPostController> _logger;

    public AutoPostController(IAutoPostService autoPost, ILogger<AutoPostController> logger)
    {
        _autoPost = autoPost;
        _logger = logger;
    }

    /// <summary>All pages: settings, today's slots with progress, totals and the last 7 days of runs.</summary>
    [HttpGet]
    public async Task<ActionResult<AutoPostOverviewDto>> Get(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _autoPost.GetOverviewAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AutoPost overview");
            return Ok(new AutoPostOverviewDto());
        }
    }

    [HttpPut("pages/{facebookAccountId:guid}/settings")]
    public async Task<ActionResult<AutoPostSettingsDto>> UpdateSettings(
        Guid facebookAccountId, [FromBody] AutoPostSettingsDto settings, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _autoPost.UpdateSettingsAsync(facebookAccountId, settings, cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Suggested posting times (Vietnam) for a number of posts per day; offset shifts them (minutes).</summary>
    [HttpGet("suggest-slots")]
    public ActionResult<List<string>> SuggestSlots([FromQuery] int count = 5, [FromQuery] int offset = 0) =>
        Ok(AutoPostService.SuggestSlots(count, offset));

    /// <summary>Makes and posts one video for the page right away, outside the schedule.</summary>
    [HttpPost("pages/{facebookAccountId:guid}/run-now")]
    public async Task<ActionResult<AutoPostRunDto>> RunNow(Guid facebookAccountId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _autoPost.RunNowAsync(facebookAccountId, cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("runs/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        await _autoPost.CancelRunAsync(id, cancellationToken)
            ? NoContent()
            : BadRequest(new { message = "Lượt này đã đăng xong hoặc đang đăng nên không huỷ được." });
}
