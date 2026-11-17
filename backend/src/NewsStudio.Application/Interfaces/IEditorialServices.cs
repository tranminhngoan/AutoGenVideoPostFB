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

/// <summary>"Kiểm chứng" step: free checks + AI editorial pass + picture checks → publication gate.</summary>
public interface IContentGateService
{
    /// <param name="runEditorial">false = reuse the stored AI editorial pass (no AI call).</param>
    Task<FactCheckReportDto> CheckAsync(Guid videoProjectId, bool runEditorial = true, CancellationToken cancellationToken = default);
    Task<FactCheckReportDto> GetAsync(Guid videoProjectId, CancellationToken cancellationToken = default);
}
