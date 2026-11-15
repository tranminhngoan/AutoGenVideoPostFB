using Microsoft.AspNetCore.Mvc;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Api.Controllers;

/// <summary>"Tin đang hot" — trending news collected from Vietnamese and international RSS feeds.</summary>
[ApiController]
[Route("api/trending")]
[Produces("application/json")]
public class TrendingController : ControllerBase
{
    private readonly ITrendingNewsService _trending;

    public TrendingController(ITrendingNewsService trending)
    {
        _trending = trending;
    }

    /// <summary>Ranked hot stories. region = all | VN | World; hours = look-back window (1–72).</summary>
    [HttpGet]
    public async Task<ActionResult<TrendingOverviewDto>> Get(
        [FromQuery] string region = "all",
        [FromQuery] string? source = null,
        [FromQuery] string? q = null,
        [FromQuery] int hours = 24,
        [FromQuery] int limit = 60,
        CancellationToken cancellationToken = default)
    {
        var result = await _trending.GetTrendingAsync(new TrendingQuery
        {
            Region = region,
            Source = source,
            Search = q,
            Hours = hours,
            Limit = limit
        }, cancellationToken);
        return Ok(result);
    }

    /// <summary>Fetches all feeds now (the Worker also does this automatically every 30 minutes).</summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<TrendingRefreshResultDto>> Refresh(CancellationToken cancellationToken)
    {
        return Ok(await _trending.RefreshAsync(cancellationToken));
    }
}
