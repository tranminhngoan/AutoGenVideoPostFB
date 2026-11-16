using Microsoft.AspNetCore.Mvc;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Infrastructure.Services;

namespace NewsStudio.Api.Controllers;

/// <summary>"Tự động đăng" — daily schedule that turns the hottest stories into videos and posts them to Facebook.</summary>
[ApiController]
[Route("api/auto-post")]
[Produces("application/json")]
public class AutoPostController : ControllerBase
{
    private readonly IAutoPostService _autoPost;

    public AutoPostController(IAutoPostService autoPost)
    {
        _autoPost = autoPost;
    }

    /// <summary>Settings, today's time slots with their progress, and the last 7 days of runs.</summary>
    [HttpGet]
    public async Task<ActionResult<AutoPostOverviewDto>> Get(CancellationToken cancellationToken) =>
        Ok(await _autoPost.GetOverviewAsync(cancellationToken));

    [HttpPut("settings")]
    public async Task<ActionResult<AutoPostSettingsDto>> UpdateSettings([FromBody] AutoPostSettingsDto settings, CancellationToken cancellationToken) =>
        Ok(await _autoPost.UpdateSettingsAsync(settings, cancellationToken));

    /// <summary>Suggested posting times (Vietnam) for a number of posts per day.</summary>
    [HttpGet("suggest-slots")]
    public ActionResult<List<string>> SuggestSlots([FromQuery] int count = 5) =>
        Ok(AutoPostService.SuggestSlots(count));

    /// <summary>Makes and posts one video right away, outside the schedule.</summary>
    [HttpPost("run-now")]
    public async Task<ActionResult<AutoPostRunDto>> RunNow(CancellationToken cancellationToken) =>
        Ok(await _autoPost.RunNowAsync(cancellationToken));

    [HttpPost("runs/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        await _autoPost.CancelRunAsync(id, cancellationToken)
            ? NoContent()
            : BadRequest(new { message = "Lượt này đã đăng xong hoặc đang đăng nên không huỷ được." });
}
