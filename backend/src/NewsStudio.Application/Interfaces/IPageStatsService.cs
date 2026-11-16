using NewsStudio.Application.DTOs;

namespace NewsStudio.Application.Interfaces;

/// <summary>Collects Reels views from Facebook and summarises them per Page.</summary>
public interface IPageStatsService
{
    /// <summary>Per-page views: totals, today, last 7 days and a day-by-day series of <paramref name="days"/> days.</summary>
    Task<PageStatsOverviewDto> GetOverviewAsync(int days = 14, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the latest numbers of published Reels from Facebook (recurring job). Fresh videos are read
    /// hourly, older ones less often; <paramref name="force"/> reads everything not read in the last 15 minutes.
    /// </summary>
    Task<PageStatsSyncResultDto> SyncAsync(bool force = false, CancellationToken cancellationToken = default);
}
