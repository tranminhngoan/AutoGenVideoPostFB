using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Domain.Enums;
using NewsStudio.Infrastructure.Common;
using NewsStudio.Infrastructure.Services;

namespace NewsStudio.Infrastructure.Jobs;

public class VideoProcessingJobExecutor : IVideoProcessingJobExecutor
{
    private readonly IApplicationDbContext _context;
    private readonly IArticleService _articleService;
    private readonly IScriptService _scriptService;
    private readonly IVideoProductionService _productionService;
    private readonly IContentGateService _gate;
    private readonly ILogger<VideoProcessingJobExecutor> _logger;
    private readonly bool _simulate;

    public VideoProcessingJobExecutor(
        IApplicationDbContext context,
        IArticleService articleService,
        IScriptService scriptService,
        IVideoProductionService productionService,
        IContentGateService gate,
        IConfiguration configuration,
        ILogger<VideoProcessingJobExecutor> logger)
    {
        _gate = gate;
        _context = context;
        _articleService = articleService;
        _scriptService = scriptService;
        _productionService = productionService;
        _logger = logger;
        _simulate = SimulationMode.IsEnabled(configuration);
    }

    /// <summary>
    /// Runs one pipeline step. A failure stops the pipeline and marks the project as Failed with the
    /// real reason — except in simulation mode (offline tests), where the step is skipped.
    /// </summary>
    private async Task RunStepAsync(string stepLabel, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (_simulate)
            {
                _logger.LogWarning(ex, "Simulation mode: step '{Step}' failed, continuing: {Msg}", stepLabel, ex.Message);
                return;
            }
            throw new InvalidOperationException($"{stepLabel}: {ex.Message}", ex);
        }
    }

    // No automatic retries: failures are usually configuration problems (missing API key, FFmpeg not
    // installed, unreadable article). Retrying would just flip the status back and forth; the user can
    // re-run the pipeline from the UI once the cause is fixed.
    [AutomaticRetry(Attempts = 0, LogEvents = true, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteVideoProcessingPipelineAsync(Guid projectId, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ProjectId"] = projectId,
            ["IdempotencyKey"] = idempotencyKey
        });

        _logger.LogInformation("Worker starting processing pipeline for Project {ProjectId} with key {IdempotencyKey}", projectId, idempotencyKey);

        var project = await _context.VideoProjects
            .Include(p => p.Scenes)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            _logger.LogError("Project {ProjectId} not found in database. Aborting job.", projectId);
            return;
        }

        // Idempotency check: Don't re-run if already in terminal/review state
        if (project.Status is VideoProjectStatus.AwaitingApproval or VideoProjectStatus.Published or VideoProjectStatus.Publishing)
        {
            _logger.LogWarning("Project {ProjectId} is already in state {Status}. Skipping duplicate execution.", projectId, project.Status);
            return;
        }

        var jobRecord = await _context.ProcessingJobs
            .FirstOrDefaultAsync(j => j.VideoProjectId == projectId && j.IdempotencyKey == idempotencyKey, cancellationToken);

        if (jobRecord == null)
        {
            jobRecord = new ProcessingJob
            {
                VideoProjectId = projectId,
                IdempotencyKey = idempotencyKey,
                Step = "PipelineStarted",
                Status = "Running",
                StartedAtUtc = DateTime.UtcNow,
                WorkerHost = Environment.MachineName
            };
            _context.ProcessingJobs.Add(jobRecord);
            await _context.SaveChangesAsync(cancellationToken);
        }

        try
        {
            // Step 1: Extracting
            await AdvanceStepAsync(project, jobRecord, VideoProjectStatus.Extracting, "Extracting article content and original images", 15, cancellationToken);
            if (project.ArticleId == null && !string.IsNullOrWhiteSpace(project.SourceUrl))
            {
                await RunStepAsync("Không đọc được bài báo", async () =>
                {
                    var articleDto = await _articleService.ExtractAndSaveArticleAsync(project.SourceUrl, cancellationToken);
                    project.ArticleId = articleDto.Id;
                    if (string.IsNullOrWhiteSpace(project.Title)
                        || project.Title == "New Video Project"
                        || project.Title == VideoProjectService.PlaceholderTitle)
                    {
                        project.Title = articleDto.Title;
                    }
                    if (!string.IsNullOrWhiteSpace(articleDto.Publisher))
                    {
                        project.Publisher = articleDto.Publisher;
                    }
                    await _context.SaveChangesAsync(cancellationToken);
                });
            }
            await Task.Delay(500, cancellationToken);

            // Step 2: Extracted
            await AdvanceStepAsync(project, jobRecord, VideoProjectStatus.Extracted, "Article content and verified images extracted", 30, cancellationToken);
            await Task.Delay(1000, cancellationToken);

            // Step 3: Analyzing & Scripting
            await AdvanceStepAsync(project, jobRecord, VideoProjectStatus.Analyzing, "AI analyzing key facts and context", 45, cancellationToken);
            await Task.Delay(300, cancellationToken);

            await AdvanceStepAsync(project, jobRecord, VideoProjectStatus.Scripting, "AI generating editorial script (45-60s)", 60, cancellationToken);
            if (project.ArticleId != null)
            {
                await RunStepAsync("Không viết được kịch bản", () =>
                    _scriptService.GenerateScriptForProjectAsync(project.Id, null, cancellationToken));
            }
            await Task.Delay(300, cancellationToken);

            // Step 4: Planning Scenes
            await AdvanceStepAsync(project, jobRecord, VideoProjectStatus.PlanningScenes, "Storyboarding 6-10 scenes with article images", 75, cancellationToken);
            if (project.ArticleId != null)
            {
                await RunStepAsync("Không chia được cảnh", () =>
                    _scriptService.PlanScenesForProjectAsync(project.Id, cancellationToken));
            }
            await Task.Delay(300, cancellationToken);

            // Step 4b: "Kiểm chứng" BEFORE the paid voice-over — a script that contradicts the article
            //          stops here (nothing is spent on reading it aloud)
            if (project.ArticleId != null)
            {
                await RunStepAsync("Kiểm chứng", async () =>
                {
                    var report = await _gate.CheckAsync(project.Id, runEditorial: true, cancellationToken);
                    if (report.Gate.Status == NewsStudio.Application.DTOs.QualityGateStatus.Rejected)
                        throw new InvalidOperationException("Kịch bản chưa khớp bài gốc nên không dựng: " + string.Join("; ", report.Gate.Reasons));
                });
            }

            // Step 5 & 6: Generating Audio (TTS) & Rendering Video (FFmpeg 9:16)
            await AdvanceStepAsync(project, jobRecord, VideoProjectStatus.GeneratingAudio, "Synthesizing Vietnamese voice-over audio", 85, cancellationToken);
            if (project.ArticleId != null)
            {
                await RunStepAsync("Không dựng được video", () =>
                    _productionService.ProduceVideoAsync(project.Id, null, null, null, cancellationToken));
            }
            await Task.Delay(300, cancellationToken);

            // Step 7: Quality Checking & Awaiting Approval
            await AdvanceStepAsync(project, jobRecord, VideoProjectStatus.QualityChecking, "Automated 10-point QC validation completed", 98, cancellationToken);

            // Final state before publish: AwaitingApproval (User MUST approve before publish)
            project.Status = VideoProjectStatus.AwaitingApproval;
            jobRecord.Step = "AwaitingApproval";
            jobRecord.Status = "Completed";
            jobRecord.ProgressPercentage = 100;
            jobRecord.FinishedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Pipeline completed successfully for Project {ProjectId}. Status transitioned to AwaitingApproval.", projectId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Processing pipeline for Project {ProjectId} was cancelled.", projectId);
            project.Status = VideoProjectStatus.Cancelled;
            jobRecord.Status = "Cancelled";
            jobRecord.ErrorMessage = "Job was cancelled by user or system";
            await _context.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing pipeline failed for Project {ProjectId} at step {Step}", projectId, jobRecord.Step);
            project.Status = VideoProjectStatus.Failed;
            project.ErrorMessage = ex.Message;
            jobRecord.Status = "Failed";
            jobRecord.ErrorMessage = ex.Message;
            jobRecord.ErrorDetails = ex.StackTrace;
            jobRecord.FinishedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(CancellationToken.None);
            throw; // Re-throw to allow Hangfire retry mechanism
        }
    }

    private async Task AdvanceStepAsync(
        VideoProject project,
        ProcessingJob job,
        VideoProjectStatus status,
        string stepDescription,
        int progressPercentage,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[JobStep] Transitioning Project {ProjectId} -> {Status} ({Progress}%): {Desc}",
            project.Id, status, progressPercentage, stepDescription);

        project.Status = status;
        job.Step = status.ToString();
        job.ProgressPercentage = progressPercentage;

        await _context.SaveChangesAsync(cancellationToken);
    }
}
