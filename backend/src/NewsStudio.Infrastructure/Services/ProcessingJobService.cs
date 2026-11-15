using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Infrastructure.Services;

public class ProcessingJobService : IProcessingJobService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<ProcessingJobService> _logger;

    public ProcessingJobService(
        IApplicationDbContext context,
        ILogger<ProcessingJobService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ProcessingJobResponseDto?> GetJobByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await _context.ProcessingJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (job == null) return null;

        return new ProcessingJobResponseDto
        {
            Id = job.Id,
            VideoProjectId = job.VideoProjectId,
            HangfireJobId = job.HangfireJobId,
            Step = job.Step,
            Status = job.Status,
            RetryCount = job.RetryCount,
            ProgressPercentage = job.ProgressPercentage,
            StartedAtUtc = job.StartedAtUtc,
            FinishedAtUtc = job.FinishedAtUtc,
            ErrorMessage = job.ErrorMessage
        };
    }

    public async Task<bool> CancelJobAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await _context.ProcessingJobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (job == null) return false;

        _logger.LogInformation("Cancelling processing job {JobId} (Hangfire ID: {HangfireJobId})", job.Id, job.HangfireJobId);

        if (!string.IsNullOrEmpty(job.HangfireJobId))
        {
            try
            {
                BackgroundJob.Delete(job.HangfireJobId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete Hangfire job {HangfireJobId}", job.HangfireJobId);
            }
        }

        job.Status = "Cancelled";
        job.FinishedAtUtc = DateTime.UtcNow;

        var project = await _context.VideoProjects.FirstOrDefaultAsync(p => p.Id == job.VideoProjectId, cancellationToken);
        if (project != null && project.Status != VideoProjectStatus.Published)
        {
            project.Status = VideoProjectStatus.Cancelled;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
