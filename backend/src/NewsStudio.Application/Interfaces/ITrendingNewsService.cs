using NewsStudio.Application.DTOs;

namespace NewsStudio.Application.Interfaces;

/// <summary>Collects and ranks "hot" news from public Vietnamese and international RSS feeds.</summary>
public interface ITrendingNewsService
{
    /// <summary>Fetches every feed, stores new items and recomputes story clusters / trend matches.</summary>
    Task<TrendingRefreshResultDto> RefreshAsync(CancellationToken cancellationToken = default);

    Task<TrendingOverviewDto> GetTrendingAsync(TrendingQuery query, CancellationToken cancellationToken = default);
}
