using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Application.Interfaces;

public interface IAIEditorService
{
    AiProviderType ProviderType { get; }
    Task<EditorialScriptResult> GenerateEditorialScriptAsync(
        EditorialScriptPromptInput input,
        string? modelName = null,
        CancellationToken cancellationToken = default);
}

public interface IScriptPlannerService
{
    Task<List<VideoScenePlanDto>> PlanScenesAsync(
        ScriptPlanningInput input,
        CancellationToken cancellationToken = default);
}

public interface IAiEditorFactory
{
    IAIEditorService GetEditorService(AiProviderType providerType);
    IAIEditorService GetEditorService(string? providerName);
}

public interface IScriptService
{
    Task<VideoScriptResponseDto> GenerateScriptForProjectAsync(
        Guid projectId,
        GenerateScriptRequestDto? options = null,
        CancellationToken cancellationToken = default);

    Task<VideoScriptResponseDto?> GetScriptByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<VideoScriptResponseDto> UpdateScriptAsync(
        Guid projectId,
        UpdateScriptRequestDto dto,
        CancellationToken cancellationToken = default);

    Task<List<VideoScenePlanDto>> PlanScenesForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<List<VideoScenePlanDto>> GetScenesByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<List<VideoScenePlanDto>> UpdateScenesAsync(
        Guid projectId,
        UpdateScenesRequestDto dto,
        CancellationToken cancellationToken = default);
}
