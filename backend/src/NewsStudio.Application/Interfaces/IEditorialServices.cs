using NewsStudio.Application.DTOs;

namespace NewsStudio.Application.Interfaces;

/// <summary>AI editorial pass before posting (headline vs article check, caption, first comment).</summary>
public interface IEditorialAssistantService
{
    Task<EditorialPassDto> RunAsync(Guid videoProjectId, CancellationToken cancellationToken = default);
}

/// <summary>Learns from a page's own view numbers: best hours, kinds of stories, regions.</summary>
public interface IPageInsightsService
{
    Task<PageInsightsDto> GetAsync(Guid facebookAccountId, CancellationToken cancellationToken = default);
}
