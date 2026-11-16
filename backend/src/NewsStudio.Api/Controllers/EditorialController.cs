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

    public EditorialController(IEditorialAssistantService editorial, IPageInsightsService insights)
    {
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

    /// <summary>Best posting hours, kinds of stories and regions for a page, from its own Reels views.</summary>
    [HttpGet("api/auto-post/pages/{facebookAccountId:guid}/insights")]
    public async Task<ActionResult<PageInsightsDto>> Insights(Guid facebookAccountId, CancellationToken cancellationToken)
        => Ok(await _insights.GetAsync(facebookAccountId, cancellationToken));
}
