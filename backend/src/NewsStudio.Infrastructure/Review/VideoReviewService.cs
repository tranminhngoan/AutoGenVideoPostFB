using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Infrastructure.Review;

public class VideoReviewService : IVideoReviewService
{
    private readonly IApplicationDbContext _context;
    private readonly IQualityControllerService _qualityController;
    private readonly IVideoAssetService _assetService;
    private readonly ILogger<VideoReviewService> _logger;

    public VideoReviewService(
        IApplicationDbContext context,
        IQualityControllerService qualityController,
        IVideoAssetService assetService,
        ILogger<VideoReviewService> logger)
    {
        _context = context;
        _qualityController = qualityController;
        _assetService = assetService;
        _logger = logger;
    }

    public async Task<VideoReviewDto> GetProjectReviewAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await _context.VideoProjects
            .Include(p => p.Reviews)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            throw new ArgumentException($"Video project with ID {projectId} not found");
        }

        var review = project.Reviews.OrderByDescending(r => r.CreatedAtUtc).FirstOrDefault();
        if (review == null)
        {
            // Create initial pending review
            review = new VideoReview
            {
                VideoProjectId = projectId,
                ReviewedBy = "System",
                Decision = "Pending",
                Status = "Pending"
            };
            _context.VideoReviews.Add(review);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var assessment = await _qualityController.EvaluateProjectQualityAsync(projectId, cancellationToken);

        // Can publish if approved by editor and no fatal violations
        var canPublish = review.Decision == "Approved" && !assessment.HasFatalViolations;

        return new VideoReviewDto
        {
            Id = review.Id,
            VideoProjectId = projectId,
            ReviewedBy = review.ReviewedBy,
            Decision = review.Decision,
            FeedbackNotes = review.FeedbackNotes,
            InternalQualityScore = assessment.TotalScore,
            SeriousViolationsFound = assessment.HasFatalViolations,
            ViolationDetails = assessment.HasFatalViolations ? string.Join("; ", assessment.FatalViolations) : null,
            CanPublish = canPublish,
            ReviewedAtUtc = review.ReviewedAtUtc,
            Assessment = assessment
        };
    }

    public async Task<VideoReviewDto> RunQualityCheckAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Running quality control check for VideoProject {ProjectId}", projectId);

        var project = await _context.VideoProjects
            .Include(p => p.Reviews)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            throw new ArgumentException($"Video project with ID {projectId} not found");
        }

        var assessment = await _qualityController.EvaluateProjectQualityAsync(projectId, cancellationToken);

        var review = project.Reviews.OrderByDescending(r => r.CreatedAtUtc).FirstOrDefault();
        if (review == null)
        {
            review = new VideoReview
            {
                VideoProjectId = projectId,
                ReviewedBy = "QualityController",
                Decision = "Pending",
                Status = "QualityChecked"
            };
            _context.VideoReviews.Add(review);
        }

        review.InternalQualityScore = assessment.TotalScore;
        review.SeriousViolationsFound = assessment.HasFatalViolations;
        review.ViolationDetails = JsonSerializer.Serialize(assessment.FatalViolations);
        review.Status = "QualityChecked";
        review.UpdatedAtUtc = DateTime.UtcNow;

        project.Status = VideoProjectStatus.AwaitingApproval;
        project.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        var canPublish = review.Decision == "Approved" && !assessment.HasFatalViolations;

        return new VideoReviewDto
        {
            Id = review.Id,
            VideoProjectId = projectId,
            ReviewedBy = review.ReviewedBy,
            Decision = review.Decision,
            FeedbackNotes = review.FeedbackNotes,
            InternalQualityScore = assessment.TotalScore,
            SeriousViolationsFound = assessment.HasFatalViolations,
            ViolationDetails = review.ViolationDetails,
            CanPublish = canPublish,
            ReviewedAtUtc = review.ReviewedAtUtc,
            Assessment = assessment
        };
    }

    public async Task<VideoReviewDto> SubmitDecisionAsync(Guid projectId, SubmitReviewDecisionRequestDto request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Submitting review decision '{Decision}' for VideoProject {ProjectId} by {Reviewer}",
            request.Decision, projectId, request.ReviewedBy);

        var project = await _context.VideoProjects
            .Include(p => p.Reviews)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            throw new ArgumentException($"Video project with ID {projectId} not found");
        }

        var assessment = await _qualityController.EvaluateProjectQualityAsync(projectId, cancellationToken);

        // FATAL VIOLATION ENFORCEMENT: Blocking approval if fatal violations exist!
        if (request.Decision.Equals("Approved", StringComparison.OrdinalIgnoreCase) && assessment.HasFatalViolations)
        {
            var reasons = string.Join(", ", assessment.FatalViolations);
            _logger.LogWarning("Approval blocked for project {ProjectId} due to fatal violations: {Reasons}", projectId, reasons);
            throw new InvalidOperationException($"Không thể phê duyệt video vì còn tồn tại lỗi nghiêm trọng chặn xuất bản: {reasons}");
        }

        var review = project.Reviews.OrderByDescending(r => r.CreatedAtUtc).FirstOrDefault();
        if (review == null)
        {
            review = new VideoReview
            {
                VideoProjectId = projectId,
                Status = "Reviewed"
            };
            _context.VideoReviews.Add(review);
        }

        review.Decision = request.Decision;
        review.ReviewedBy = string.IsNullOrWhiteSpace(request.ReviewedBy) ? "Editor" : request.ReviewedBy;
        review.FeedbackNotes = request.FeedbackNotes;
        review.ReviewedAtUtc = DateTime.UtcNow;
        review.InternalQualityScore = assessment.TotalScore;
        review.SeriousViolationsFound = assessment.HasFatalViolations;
        review.ViolationDetails = assessment.HasFatalViolations ? JsonSerializer.Serialize(assessment.FatalViolations) : null;
        review.UpdatedAtUtc = DateTime.UtcNow;

        // Update project status according to decision.
        // "Needs…" decisions only record the reviewer's request. They must NOT move the project into a
        // processing state (Analyzing / GeneratingAudio …): nothing is actually running at that point,
        // and the real status is set by the script/production services when the work is performed.
        switch (request.Decision.ToLowerInvariant())
        {
            case "approved":
            case "rejected":
                project.Status = VideoProjectStatus.AwaitingApproval;
                break;
        }
        project.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        var canPublish = review.Decision.Equals("Approved", StringComparison.OrdinalIgnoreCase) && !assessment.HasFatalViolations;

        return new VideoReviewDto
        {
            Id = review.Id,
            VideoProjectId = projectId,
            ReviewedBy = review.ReviewedBy,
            Decision = review.Decision,
            FeedbackNotes = review.FeedbackNotes,
            InternalQualityScore = assessment.TotalScore,
            SeriousViolationsFound = assessment.HasFatalViolations,
            ViolationDetails = review.ViolationDetails,
            CanPublish = canPublish,
            ReviewedAtUtc = review.ReviewedAtUtc,
            Assessment = assessment
        };
    }

    public async Task<bool> UpdateMetadataAsync(Guid projectId, UpdateVideoMetadataRequestDto request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating metadata for VideoProject {ProjectId}", projectId);

        var project = await _context.VideoProjects
            .Include(p => p.Script)
            .Include(p => p.Scenes)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            throw new ArgumentException($"Video project with ID {projectId} not found");
        }

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            project.Title = request.Title;
            if (project.Script != null)
            {
                project.Script.Title = request.Title;
            }
        }

        if (project.Script != null && !string.IsNullOrWhiteSpace(request.Hook))
        {
            project.Script.Hook = request.Hook;
        }

        if (!string.IsNullOrWhiteSpace(request.Caption))
        {
            var firstScene = project.Scenes.OrderBy(s => s.SceneNumber).FirstOrDefault();
            if (firstScene != null)
            {
                firstScene.OnScreenText = request.Caption;
            }
        }

        project.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RequestActionAsync(Guid projectId, string action, string? notes = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Requesting action '{Action}' for VideoProject {ProjectId}", action, projectId);

        var decision = action.ToLowerInvariant() switch
        {
            "regeneratescript" or "script" => "NeedsScriptRegen",
            "regenerateaudio" or "audio" => "NeedsAudioRegen",
            "rerender" or "render" => "NeedsReRender",
            _ => throw new ArgumentException($"Hành động không hợp lệ: {action}")
        };

        var request = new SubmitReviewDecisionRequestDto
        {
            Decision = decision,
            ReviewedBy = "Editor",
            FeedbackNotes = notes ?? $"Yêu cầu thực hiện lại tác vụ {action}"
        };

        await SubmitDecisionAsync(projectId, request, cancellationToken);
        return true;
    }

    public async Task<bool> DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting VideoProject {ProjectId} and cleaning up all associated assets", projectId);

        var project = await _context.VideoProjects
            .Include(p => p.Assets)
            .Include(p => p.Reviews)
            .Include(p => p.Scenes)
            .Include(p => p.Script)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            return false;
        }

        // Clean up ephemeral assets from disk and memory
        await _assetService.CleanupProjectAssetsAsync(projectId, cancellationToken);

        _context.VideoProjects.Remove(project);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
