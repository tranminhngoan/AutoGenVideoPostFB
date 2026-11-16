using NewsStudio.Application.DTOs;

namespace NewsStudio.Application.Interfaces;

/// <summary>
/// "Tự động đăng": for every connected Facebook Page, picks the hottest unused stories of the page's topic,
/// makes videos and posts them to that page on its daily schedule.
/// </summary>
public interface IAutoPostService
{
    /// <summary>All pages with their schedule and today's progress, plus totals and the last 7 days.</summary>
    Task<AutoPostOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves one page's settings. Throws KeyNotFoundException when the page isn't connected.</summary>
    Task<AutoPostSettingsDto> UpdateSettingsAsync(Guid facebookAccountId, AutoPostSettingsDto settings, CancellationToken cancellationToken = default);

    /// <summary>Makes and posts one video for the page right away (outside the schedule).</summary>
    Task<AutoPostRunDto> RunNowAsync(Guid facebookAccountId, CancellationToken cancellationToken = default);

    /// <summary>Stops a run that hasn't been posted yet. The video (if any) stays for manual review.</summary>
    Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Recurring job (every 2 minutes): starts due slots of every page and moves running ones forward.</summary>
    Task TickAsync(CancellationToken cancellationToken = default);
}
