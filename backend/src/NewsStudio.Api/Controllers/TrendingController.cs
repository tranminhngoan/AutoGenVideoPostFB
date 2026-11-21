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
    private readonly ILogger<TrendingController> _logger;

    public TrendingController(ITrendingNewsService trending, ILogger<TrendingController> logger)
    {
        _trending = trending;
        _logger = logger;
    }

    /// <summary>Ranked hot stories. region = all | VN | World; hours = look-back window (1–72).</summary>
    [HttpGet]
    public async Task<ActionResult<TrendingOverviewDto>> Get(
        [FromQuery] string region = "all",
        [FromQuery] string? source = null,
        [FromQuery] string? q = null,
        [FromQuery] int hours = 24,
        [FromQuery] int limit = 60,
        [FromQuery] string? topic = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _trending.GetTrendingAsync(new TrendingQuery
            {
                Region = region,
                Source = source,
                Search = q,
                Hours = hours,
                Limit = limit,
                Topic = topic
            }, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get trending news (region={Region}, hours={Hours})", region, hours);
            return Ok(new TrendingOverviewDto
            {
                Items = new List<TrendingArticleDto>(),
                Sources = new List<TrendingSourceDto>(),
                TrendKeywords = new List<string>()
            });
        }
    }

    /// <summary>News topics a Page can be dedicated to (key, Vietnamese name, description).</summary>
    [HttpGet("topics")]
    public ActionResult<List<NewsTopicDto>> Topics()
    {
        try
        {
            return Ok(NewsStudio.Infrastructure.Trending.NewsTopics.All
                .Select(t => new NewsTopicDto { Key = t.Key, Name = t.Name, Description = t.Description })
                .ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list trending topics");
            return Ok(new List<NewsTopicDto>());
        }
    }

    /// <summary>Fetches all feeds now (the Worker also does this automatically every 30 minutes).</summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<TrendingRefreshResultDto>> Refresh(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _trending.RefreshAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to manually refresh trending feeds");
            return Ok(new TrendingRefreshResultDto
            {
                StartedAtUtc = DateTime.UtcNow,
                FinishedAtUtc = DateTime.UtcNow,
                FeedsFailed = 1,
                FailedFeeds = new List<string> { ex.Message }
            });
        }
    }
}
