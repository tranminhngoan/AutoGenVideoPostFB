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

    /// <summary>
    /// Title used until the article has been extracted; the pipeline replaces it with the real article title.
    /// </summary>
    public const string PlaceholderTitle = "Bản tin Reels tự động";

    public async Task<VideoProjectResponseDto> CreateProjectAsync(CreateVideoProjectDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating new VideoProject from URL: {Url}", dto.SourceUrl);

        // The page the video is made for: its name goes on the video, its topic picks the colours
        var pages = await _context.FacebookAccounts.AsNoTracking()
            .Where(a => a.IsActive)
            .Select(a => new { a.Id, a.PageName })
            .ToListAsync(cancellationToken);
        Guid? pageId = dto.FacebookAccountId is Guid requested && pages.Any(p => p.Id == requested)
            ? requested
            : pages.Count == 1 ? pages[0].Id : null;
        string? topic = null;
        if (pageId != null)
        {
            topic = await _context.AutoPostSettings.AsNoTracking()
                .Where(s => s.FacebookAccountId == pageId)
                .Select(s => s.Topic)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var project = new VideoProject
        {
            Title = string.IsNullOrWhiteSpace(dto.Title) ? PlaceholderTitle : dto.Title.Trim(),
            SourceUrl = dto.SourceUrl,
            Publisher = string.IsNullOrWhiteSpace(dto.Publisher) ? "Báo điện tử" : dto.Publisher,
            TargetDurationSeconds = dto.TargetDurationSeconds > 0 ? dto.TargetDurationSeconds : 105,
            AiProvider = string.IsNullOrWhiteSpace(dto.AiProvider) ? null : dto.AiProvider,
            Voice = string.IsNullOrWhiteSpace(dto.Voice) ? null : dto.Voice,
            VoiceSpeed = string.IsNullOrWhiteSpace(dto.VoiceSpeed) ? null : dto.VoiceSpeed,
            FacebookAccountId = pageId,
            Topic = topic,
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

        var created = MapToDto(project, new List<ProcessingJob> { job });
        created.PageName = pages.FirstOrDefault(p => p.Id == pageId)?.PageName;
        return created;
    }

    public async Task<IEnumerable<VideoProjectResponseDto>> GetProjectsAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var projects = await _context.VideoProjects
            .AsNoTracking()
            .Include(p => p.Scenes).ThenInclude(s => s.SelectedArticleImage)
            .Include(p => p.ProcessingJobs)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var names = await PageNamesAsync(cancellationToken);
        return projects.Select(p =>
        {
            var dto = MapToDto(p, p.ProcessingJobs);
            dto.PageName = p.FacebookAccountId != null ? names.GetValueOrDefault(p.FacebookAccountId.Value) : null;
            return dto;
        });
    }

    public async Task<VideoProjectResponseDto?> GetProjectByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await _context.VideoProjects
            .AsNoTracking()
            .Include(p => p.Scenes).ThenInclude(s => s.SelectedArticleImage)
            .Include(p => p.ProcessingJobs)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project == null) return null;
        var dto = MapToDto(project, project.ProcessingJobs);
        if (project.FacebookAccountId != null)
        {
            dto.PageName = await _context.FacebookAccounts.AsNoTracking()
                .Where(a => a.Id == project.FacebookAccountId)
                .Select(a => a.PageName)
                .FirstOrDefaultAsync(cancellationToken);
        }
        return dto;
    }

    public async Task<ProcessingJobResponseDto> EnqueueProjectProcessingAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await _context.VideoProjects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project == null)
        {
            throw new KeyNotFoundException($"VideoProject {projectId} not found.");
        }

        var idempotencyKey = Guid.NewGuid().ToString();

        // Reuse the placeholder job created together with the project instead of leaving it
        // behind as an orphan "Pending" record that never runs.
        var job = await _context.ProcessingJobs
            .Where(j => j.VideoProjectId == projectId && j.Status == "Pending" && j.HangfireJobId == null)
            .OrderBy(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (job == null)
        {
            job = new ProcessingJob { VideoProjectId = projectId };
            _context.ProcessingJobs.Add(job);
        }

        job.IdempotencyKey = idempotencyKey;
        job.Step = "Extracting";
        job.Status = "Running";
        job.StartedAtUtc = DateTime.UtcNow;
        job.WorkerHost = Environment.MachineName;
        project.Status = VideoProjectStatus.Extracting;
        project.ErrorMessage = null;

        // Persist the job record before enqueueing so the worker always finds it by idempotency key
        await _context.SaveChangesAsync(cancellationToken);

        var hangfireJobId = _backgroundJobClient.Enqueue<IVideoProcessingJobExecutor>(
            executor => executor.ExecuteVideoProcessingPipelineAsync(projectId, idempotencyKey, CancellationToken.None));

        _logger.LogInformation("Enqueued Hangfire Job {JobId} for Project {ProjectId}", hangfireJobId, projectId);

        job.HangfireJobId = hangfireJobId;
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

    private Task<Dictionary<Guid, string>> PageNamesAsync(CancellationToken ct) =>
        _context.FacebookAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.PageName, ct);

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
            AiProvider = project.AiProvider,
            Voice = project.Voice,
            VoiceSpeed = project.VoiceSpeed,
            FacebookAccountId = project.FacebookAccountId,
            Topic = project.Topic,
            SceneCount = project.Scenes?.Count ?? 0,
            Scenes = project.Scenes?.OrderBy(s => s.SceneNumber).Select(s => new VideoSceneResponseDto
            {
                Id = s.Id,
                SceneNumber = s.SceneNumber,
                StartTimeSeconds = s.StartTimeSeconds,
                EndTimeSeconds = s.EndTimeSeconds,
                VoiceText = s.VoiceText,
                OnScreenText = s.OnScreenText,
                ImageUrl = s.SelectedArticleImage?.OriginalImageUrl,
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
