using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Infrastructure.Services;

public class VideoProjectService : IVideoProjectService
{
    private readonly IApplicationDbContext _context;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<VideoProjectService> _logger;

    public VideoProjectService(
        IApplicationDbContext context,
        IBackgroundJobClient backgroundJobClient,
        ILogger<VideoProjectService> logger)
    {
        _context = context;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    public async Task<VideoProjectResponseDto> CreateProjectAsync(CreateVideoProjectDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating new VideoProject from URL: {Url}", dto.SourceUrl);

        var project = new VideoProject
        {
            Title = string.IsNullOrWhiteSpace(dto.Title) ? "Bản tin Reels tự động" : dto.Title,
            SourceUrl = dto.SourceUrl,
            Publisher = string.IsNullOrWhiteSpace(dto.Publisher) ? "Báo điện tử" : dto.Publisher,
            TargetDurationSeconds = dto.TargetDurationSeconds > 0 ? dto.TargetDurationSeconds : 50,
            Status = VideoProjectStatus.Pending,
            Resolution = "1080x1920",
            AspectRatio = "9:16",
            Fps = 30
        };

        _context.VideoProjects.Add(project);
        await _context.SaveChangesAsync(cancellationToken);

        // Auto-create initial processing job record
        var job = new ProcessingJob
        {
            VideoProjectId = project.Id,
            Step = "Pending",
            Status = "Pending",
            IdempotencyKey = Guid.NewGuid().ToString()
        };
        _context.ProcessingJobs.Add(job);
        await _context.SaveChangesAsync(cancellationToken);

        return MapToDto(project, new List<ProcessingJob> { job });
    }

    public async Task<IEnumerable<VideoProjectResponseDto>> GetProjectsAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var projects = await _context.VideoProjects
            .AsNoTracking()
            .Include(p => p.Scenes)
            .Include(p => p.ProcessingJobs)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return projects.Select(p => MapToDto(p, p.ProcessingJobs));
    }

    public async Task<VideoProjectResponseDto?> GetProjectByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await _context.VideoProjects
            .AsNoTracking()
            .Include(p => p.Scenes)
            .Include(p => p.ProcessingJobs)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return project == null ? null : MapToDto(project, project.ProcessingJobs);
    }

    public async Task<ProcessingJobResponseDto> EnqueueProjectProcessingAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await _context.VideoProjects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project == null)
        {
            throw new KeyNotFoundException($"VideoProject {projectId} not found.");
        }

        var idempotencyKey = Guid.NewGuid().ToString();

        // Enqueue job to Hangfire
        var hangfireJobId = _backgroundJobClient.Enqueue<IVideoProcessingJobExecutor>(
            executor => executor.ExecuteVideoProcessingPipelineAsync(projectId, idempotencyKey, CancellationToken.None));

        _logger.LogInformation("Enqueued Hangfire Job {JobId} for Project {ProjectId}", hangfireJobId, projectId);

        var job = new ProcessingJob
        {
            VideoProjectId = projectId,
            HangfireJobId = hangfireJobId,
            IdempotencyKey = idempotencyKey,
            Step = "Extracting",
            Status = "Running",
            StartedAtUtc = DateTime.UtcNow,
            WorkerHost = Environment.MachineName
        };

        project.Status = VideoProjectStatus.Extracting;

        _context.ProcessingJobs.Add(job);
        await _context.SaveChangesAsync(cancellationToken);

        return new ProcessingJobResponseDto
        {
            Id = job.Id,
            VideoProjectId = projectId,
            HangfireJobId = hangfireJobId,
            Step = job.Step,
            Status = job.Status,
            ProgressPercentage = job.ProgressPercentage,
            StartedAtUtc = job.StartedAtUtc
        };
    }

    private static VideoProjectResponseDto MapToDto(VideoProject project, IEnumerable<ProcessingJob>? jobs)
    {
        return new VideoProjectResponseDto
        {
            Id = project.Id,
            Title = project.Title,
            SourceUrl = project.SourceUrl,
            Publisher = project.Publisher,
            Status = project.Status.ToString(),
            TargetDurationSeconds = project.TargetDurationSeconds,
            ActualDurationSeconds = project.ActualDurationSeconds,
            Resolution = project.Resolution,
            AspectRatio = project.AspectRatio,
            CreatedAtUtc = project.CreatedAtUtc,
            UpdatedAtUtc = project.UpdatedAtUtc,
            ErrorMessage = project.ErrorMessage,
            SceneCount = project.Scenes?.Count ?? 0,
            Scenes = project.Scenes?.Select(s => new VideoSceneResponseDto
            {
                Id = s.Id,
                SceneNumber = s.SceneNumber,
                StartTimeSeconds = s.StartTimeSeconds,
                EndTimeSeconds = s.EndTimeSeconds,
                VoiceText = s.VoiceText,
                OnScreenText = s.OnScreenText,
                MotionType = s.MotionType,
                TransitionType = s.TransitionType,
                ScenePurpose = s.ScenePurpose
            }).ToList() ?? new List<VideoSceneResponseDto>(),
            Jobs = jobs?.Select(j => new ProcessingJobResponseDto
            {
                Id = j.Id,
                VideoProjectId = j.VideoProjectId,
                HangfireJobId = j.HangfireJobId,
                Step = j.Step,
                Status = j.Status,
                RetryCount = j.RetryCount,
                ProgressPercentage = j.ProgressPercentage,
                StartedAtUtc = j.StartedAtUtc,
                FinishedAtUtc = j.FinishedAtUtc,
                ErrorMessage = j.ErrorMessage
            }).OrderByDescending(j => j.StartedAtUtc).ToList() ?? new List<ProcessingJobResponseDto>()
        };
    }
}
