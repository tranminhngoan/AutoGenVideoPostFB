using System;
using System.Threading;
using System.Threading.Tasks;
using NewsStudio.Application.DTOs;

namespace NewsStudio.Application.Interfaces;

public interface IQualityControllerService
{
    Task<QualityAssessmentResultDto> EvaluateProjectQualityAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public interface IVideoReviewService
{
    Task<VideoReviewDto> GetProjectReviewAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<VideoReviewDto> RunQualityCheckAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<VideoReviewDto> SubmitDecisionAsync(Guid projectId, SubmitReviewDecisionRequestDto request, CancellationToken cancellationToken = default);
    Task<bool> UpdateMetadataAsync(Guid projectId, UpdateVideoMetadataRequestDto request, CancellationToken cancellationToken = default);
    Task<bool> RequestActionAsync(Guid projectId, string action, string? notes = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
}
