using Microsoft.AspNetCore.Mvc;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Api.Controllers;

/// <summary>AI editorial pass before posting, and what each page's audience watches most.</summary>
[ApiController]
[Produces("application/json")]
public class EditorialController : ControllerBase
{
    private readonly IEditorialAssistantService _editorial;
    private readonly IPageInsightsService _insights;
    private readonly IContentGateService _gate;

    public EditorialController(IEditorialAssistantService editorial, IPageInsightsService insights, IContentGateService gate)
    {
        _gate = gate;
        _editorial = editorial;
        _insights = insights;
    }

    /// <summary>Checks the video's headline against the article and writes the caption + first comment.</summary>
    [HttpPost("api/video-projects/{id:guid}/editorial-pass")]
    public async Task<ActionResult<EditorialPassDto>> EditorialPass(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _editorial.RunAsync(id, cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or System.Text.Json.JsonException)
        {
            return BadRequest(new { error = $"AI chưa kiểm tra được: {ex.Message}" });
        }
    }

    /// <summary>Fact sheet, AI editorial pass and publication gate of a video.</summary>
    [HttpGet("api/video-projects/{id:guid}/fact-check")]
    public async Task<ActionResult<FactCheckReportDto>> FactCheck(Guid id, CancellationToken cancellationToken)
    {
        try { return Ok(await _gate.GetAsync(id, cancellationToken)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    /// <summary>Runs the checks again (after the script or pictures were changed). ai=false: no AI call.</summary>
    [HttpPost("api/video-projects/{id:guid}/fact-check")]
    public async Task<ActionResult<FactCheckReportDto>> RunFactCheck(Guid id, [FromQuery] bool ai = true, CancellationToken cancellationToken = default)
    {
        try { return Ok(await _gate.CheckAsync(id, ai, cancellationToken)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    /// <summary>Best posting hours, kinds of stories and regions for a page, from its own Reels views.</summary>
    [HttpGet("api/auto-post/pages/{facebookAccountId:guid}/insights")]
    public async Task<ActionResult<PageInsightsDto>> Insights(Guid facebookAccountId, CancellationToken cancellationToken)
        => Ok(await _insights.GetAsync(facebookAccountId, cancellationToken));
}
