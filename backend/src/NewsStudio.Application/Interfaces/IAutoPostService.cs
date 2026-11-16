using NewsStudio.Application.DTOs;

namespace NewsStudio.Application.Interfaces;

/// <summary>"Tự động đăng": picks the hottest unused stories, makes videos and posts them to Facebook on a daily schedule.</summary>
public interface IAutoPostService
{
    Task<AutoPostOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<AutoPostSettingsDto> UpdateSettingsAsync(AutoPostSettingsDto settings, CancellationToken cancellationToken = default);

    /// <summary>Starts one automatic post right away (outside the schedule).</summary>
    Task<AutoPostRunDto> RunNowAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops a run that hasn't been posted yet. The video (if any) stays for manual review.</summary>
    Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Recurring job (every 2 minutes): starts due slots and moves running ones forward.</summary>
    Task TickAsync(CancellationToken cancellationToken = default);
}
