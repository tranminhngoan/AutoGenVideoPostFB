using Hangfire;
using Microsoft.AspNetCore.Mvc;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Api.Controllers;

/// <summary>Comments inbox: viewers' comments with AI-drafted replies, and the page's first-comment setting.</summary>
[ApiController]
[Route("api/engagement")]
public class EngagementController : ControllerBase
{
    private readonly IEngagementService _engagement;
    private readonly IBackgroundJobClient _jobs;

    public EngagementController(IEngagementService engagement, IBackgroundJobClient jobs)
    {
        _engagement = engagement;
        _jobs = jobs;
    }

    [HttpGet("comments")]
    public async Task<ActionResult<List<PostCommentDto>>> Comments([FromQuery] string status = "New", CancellationToken ct = default) =>
        Ok(await _engagement.ListCommentsAsync(status, ct));

    public sealed record ReplyRequest(string Message);

    [HttpPost("comments/{id:guid}/reply")]
    public async Task<IActionResult> Reply(Guid id, [FromBody] ReplyRequest body, CancellationToken ct)
    {
        try { return Ok(await _engagement.ReplyAsync(id, body.Message, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("comments/{id:guid}/dismiss")]
    public async Task<IActionResult> Dismiss(Guid id, CancellationToken ct)
    {
        try { await _engagement.DismissAsync(id, ct); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Reads new comments (and posts first comments) now instead of at the next half hour.</summary>
    [HttpPost("refresh")]
    public IActionResult Refresh()
    {
        _jobs.Enqueue<IPageStatsService>(s => s.SyncAsync(true, CancellationToken.None));
        _jobs.Enqueue<IEngagementService>(s => s.RunAsync(CancellationToken.None));
        return Accepted();
    }

    [HttpGet("settings")]
    public async Task<IActionResult> Settings(CancellationToken ct) => Ok(new { firstComment = await _engagement.FirstCommentEnabledAsync(ct) });

    public sealed record SettingsRequest(bool FirstComment);

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] SettingsRequest body, CancellationToken ct)
    {
        await _engagement.SetFirstCommentEnabledAsync(body.FirstComment, ct);
        return Ok(new { firstComment = body.FirstComment });
    }
}
