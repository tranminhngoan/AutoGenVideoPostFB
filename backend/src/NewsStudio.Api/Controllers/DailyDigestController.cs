using Microsoft.AspNetCore.Mvc;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Api.Controllers;

/// <summary>Daily "Điểm tin nóng" of each Facebook Page: one wall post with pictures + one Story, every evening.</summary>
[ApiController]
[Route("api/daily-digest")]
public class DailyDigestController : ControllerBase
{
    private readonly IDailyDigestService _digest;

    public DailyDigestController(IDailyDigestService digest)
    {
        _digest = digest;
    }

    /// <summary>Digests of the last 7 days (optionally of one page), newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<DailyDigestRunDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecent([FromQuery] Guid? pageId, CancellationToken cancellationToken)
        => Ok(await _digest.GetRecentAsync(pageId, cancellationToken));

    /// <summary>Makes today's digest pictures for a page without posting (to check before the evening).</summary>
    [HttpPost("pages/{id:guid}/preview")]
    [ProducesResponseType(typeof(DailyDigestRunDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Preview(Guid id, [FromQuery] string? kind, CancellationToken cancellationToken)
        => Run(() => _digest.PreviewAsync(id, kind, cancellationToken));

    /// <summary>Makes and posts today's digest for a page right now.</summary>
    [HttpPost("pages/{id:guid}/publish")]
    [ProducesResponseType(typeof(DailyDigestRunDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken)
        => Run(() => _digest.PublishNowAsync(id, cancellationToken));

    /// <summary>One picture of a digest (post_00.jpg = wall-post cover, story_01.jpg = first Story story…).</summary>
    [HttpGet("runs/{id:guid}/images/{fileName}")]
    public async Task<IActionResult> Image(Guid id, string fileName, CancellationToken cancellationToken)
    {
        var path = await _digest.GetImagePathAsync(id, fileName, cancellationToken);
        if (path == null) return NotFound(new { error = "Ảnh không có hoặc đã bị xoá." });
        return PhysicalFile(path, "image/jpeg");
    }

    private async Task<IActionResult> Run(Func<Task<DailyDigestRunDto>> action)
    {
        try
        {
            return Ok(await action());
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
}
