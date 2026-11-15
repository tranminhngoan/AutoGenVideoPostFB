using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Infrastructure.Jobs;

public class JobRecoveryService : IJobRecoveryService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<JobRecoveryService> _logger;

    public JobRecoveryService(
        IApplicationDbContext context,
        ILogger<JobRecoveryService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<JobRecoveryReportDto> RecoverDanglingJobsAsync(CancellationToken cancellationToken = default)
    {
        var report = new JobRecoveryReportDto();
        _logger.LogInformation("JobRecoveryService: Starting recovery scan for interrupted or dangling jobs...");

        // In-flight transient states that may be abandoned on worker restart
        var transientStatuses = new[]
        {
            VideoProjectStatus.Extracting,
            VideoProjectStatus.Analyzing,
            VideoProjectStatus.Scripting,
            VideoProjectStatus.PlanningScenes,
            VideoProjectStatus.GeneratingAudio,
            VideoProjectStatus.Rendering,
            VideoProjectStatus.QualityChecking,
            VideoProjectStatus.Publishing
        };

        var danglingProjects = await _context.VideoProjects
            .Where(p => transientStatuses.Contains(p.Status))
            .ToListAsync(cancellationToken);

        report.TotalDanglingProjectsExamined = danglingProjects.Count;

        foreach (var project in danglingProjects)
        {
            _logger.LogWarning("Examining dangling VideoProject ID: {Id}, Status: {Status}, Title: {Title}",
                project.Id, project.Status, project.Title);

            // 1. Guard for Publishing: Strictly prevent duplicate publishing
            if (project.Status == VideoProjectStatus.Publishing)
            {
                var existingPublishedPost = await _context.FacebookPosts
                    .FirstOrDefaultAsync(p => p.VideoProjectId == project.Id && p.UploadStatus == "Published", cancellationToken);

                if (existingPublishedPost != null)
                {
                    project.Status = VideoProjectStatus.Published;
                    project.UpdatedAtUtc = DateTime.UtcNow;
                    report.ProtectedPublishedProjectsCount++;
                    report.ActionLogs.Add($"Project {project.Id}: FacebookPost already published. Reconciled status to Published.");
                    _logger.LogInformation("Project {Id} was found already published on Facebook. Updated to Published.", project.Id);
                }
                else
                {
                    // Fallback to AwaitingApproval so editor can safely re-trigger publish
                    project.Status = VideoProjectStatus.AwaitingApproval;
                    project.UpdatedAtUtc = DateTime.UtcNow;
                    report.RecoveredToAwaitingApprovalCount++;
                    report.ActionLogs.Add($"Project {project.Id}: Publish interrupted before completion. Rolled back safely to AwaitingApproval.");
                    _logger.LogWarning("Project {Id} publish was interrupted. Rolled back to AwaitingApproval to prevent duplicate publish.", project.Id);
                }
                continue;
            }

            // 2. Handling Rendering & Audio Generation
            if (project.Status == VideoProjectStatus.Rendering || 
                project.Status == VideoProjectStatus.GeneratingAudio ||
                project.Status == VideoProjectStatus.QualityChecking)
            {
                var finalVideoAsset = await _context.VideoAssets
                    .Where(a => a.VideoProjectId == project.Id && a.AssetType == "FinalVideo" && !a.IsCleanedUp)
                    .OrderByDescending(a => a.CreatedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                bool fileExistsOnDisk = finalVideoAsset != null && 
                                        !string.IsNullOrWhiteSpace(finalVideoAsset.LocalTempPath) && 
                                        File.Exists(finalVideoAsset.LocalTempPath);

                if (fileExistsOnDisk)
                {
                    // Video file is intact on disk; can be reviewed
                    project.Status = VideoProjectStatus.AwaitingApproval;
                    project.UpdatedAtUtc = DateTime.UtcNow;
                    report.RecoveredToAwaitingApprovalCount++;
                    report.ActionLogs.Add($"Project {project.Id}: Intact video MP4 found on disk. Advanced to AwaitingApproval.");
                }
                else
                {
                    // Ephemeral files missing due to container restart: mark failed/retryable
                    project.Status = VideoProjectStatus.Failed;
                    project.ErrorMessage = "Quá trình dựng video bị ngắt quãng do Worker khởi động lại. Tệp tin tạm đã được làm mới; vui lòng bấm Tạo lại video.";
                    project.UpdatedAtUtc = DateTime.UtcNow;
                    report.MarkedFailedCount++;
                    report.ActionLogs.Add($"Project {project.Id}: Temp assets missing after restart. Marked Failed with clear retry prompt.");
                }

                continue;
            }

            // 3. Handling Early Pipeline Stages (Extracting, Analyzing, Scripting, PlanningScenes)
            var hasScript = await _context.VideoScripts
                .AnyAsync(s => s.VideoProjectId == project.Id, cancellationToken);

            if (hasScript)
            {
                project.Status = VideoProjectStatus.Pending;
                project.UpdatedAtUtc = DateTime.UtcNow;
                report.ResetToPendingCount++;
                report.ActionLogs.Add($"Project {project.Id}: Existing script preserved. Reset to Pending for re-rendering.");
            }
            else
            {
                project.Status = VideoProjectStatus.Pending;
                project.ErrorMessage = "Tiến trình biên tập kịch bản bị ngắt quãng do hệ thống khởi động lại. Đã đặt lại trạng thái chờ xử lý.";
                project.UpdatedAtUtc = DateTime.UtcNow;
                report.ResetToPendingCount++;
                report.ActionLogs.Add($"Project {project.Id}: Reset to Pending ready for re-extraction.");
            }
        }

        // Clean up or fail dangling ProcessingJobs
        var runningJobs = await _context.ProcessingJobs
            .Where(j => j.Status == "Running" || j.Status == "Enqueued")
            .ToListAsync(cancellationToken);

        foreach (var job in runningJobs)
        {
            job.Status = "Failed";
            job.ErrorMessage = "Tác vụ bị gián đoạn do Worker service khởi động lại. Đã đánh dấu để xử lý lại an toàn.";
            job.FinishedAtUtc = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("JobRecoveryService completed: {Examined} dangling projects examined. {Awaiting} awaiting approval, {Reset} reset, {Failed} failed.",
            report.TotalDanglingProjectsExamined,
            report.RecoveredToAwaitingApprovalCount,
            report.ResetToPendingCount,
            report.MarkedFailedCount);

        return report;
    }
}
