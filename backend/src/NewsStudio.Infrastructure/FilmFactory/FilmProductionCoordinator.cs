using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Infrastructure.FilmFactory;

public class FilmProductionCoordinator : IFilmProductionCoordinator
{
    private readonly IBackgroundJobClient _jobClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FilmProductionCoordinator> _logger;

    public FilmProductionCoordinator(
        IBackgroundJobClient jobClient,
        IServiceScopeFactory scopeFactory,
        ILogger<FilmProductionCoordinator> logger)
    {
        _jobClient = jobClient;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task TriggerFullAutoProductionAsync(Guid projectId, CancellationToken ct = default)
    {
        _logger.LogInformation("Enqueueing Full Auto Film Production for Project {Id} into Hangfire...", projectId);
        _jobClient.Enqueue<IFilmProductionCoordinator>(c => c.ExecuteProductionPipelineAsync(projectId, CancellationToken.None));
        return Task.CompletedTask;
    }

    [Queue("render")]
    public async Task ExecuteProductionPipelineAsync(Guid projectId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var showrunner = scope.ServiceProvider.GetRequiredService<IFilmShowrunnerService>();
        var renderer = scope.ServiceProvider.GetRequiredService<IFilmRendererService>();
        var costController = scope.ServiceProvider.GetRequiredService<IFilmCostController>();

        var project = await context.FilmProjects
            .Include(p => p.Episodes)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project == null)
        {
            _logger.LogError("Production Pipeline: Film project {Id} not found.", projectId);
            return;
        }

        try
        {
            _logger.LogInformation("================ STARTING FULL AUTO FILM PRODUCTION ================");
            _logger.LogInformation("Project: {Title} ({Id}) | Logline: {Logline}", project.Title, project.Id, project.Logline);

            // Phase 1: AI Showrunner - Develop Story Bible
            _logger.LogInformation(">>> Step 1/6: AI Showrunner developing Story Bible & 10-episode outline...");
            var bible = await showrunner.DevelopStoryBibleAsync(projectId, ct);

            // Phase 2: AI Character & Location Design
            _logger.LogInformation(">>> Step 2/6: Designing Character Bible & Location Bible...");
            var (chars, locs) = await showrunner.GenerateCharactersAndLocationsAsync(projectId, ct);

            // Reload project episodes
            var episodes = await context.FilmEpisodes
                .Where(e => e.FilmProjectId == projectId)
                .OrderBy(e => e.EpisodeNumber)
                .ToListAsync(ct);

            project.Status = "InProduction";
            project.UpdatedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync(ct);

            // Phase 3 & 4: Screenwriting & Continuity Checking for 10 Episodes
            _logger.LogInformation(">>> Step 3/6: AI Screenwriter writing 5-minute scripts for 10 episodes...");
            foreach (var ep in episodes)
            {
                _logger.LogInformation("Writing script for Episode {Num}/10...", ep.EpisodeNumber);
                await showrunner.WriteEpisodeScriptAsync(ep.Id, ct);
                await showrunner.CheckContinuityAsync(ep.Id, ct);
            }

            // Phase 5: Budget & Cost Controller Evaluation
            _logger.LogInformation(">>> Step 4/6: Cost Controller checking budget limit ($5.0 USD)...");
            var costSummary = await costController.EvaluateBudgetStatusAsync(projectId, ct);
            _logger.LogInformation("Budget Status: {Status} | Spent: ${Spent:F4} | Remaining: ${Rem:F4}",
                costSummary.BudgetStatus, costSummary.TotalSpentUsd, costSummary.RemainingBudgetUsd);

            // Phase 6: Rendering 10 Episodes
            _logger.LogInformation(">>> Step 5/6: Film Renderer assembling video files for 10 episodes...");
            int completedCount = 0;
            foreach (var ep in episodes)
            {
                try
                {
                    _logger.LogInformation("Rendering Episode {Num}/10...", ep.EpisodeNumber);
                    var videoPath = await renderer.AssembleEpisodeAsync(ep.Id, ct);
                    completedCount++;
                    _logger.LogInformation("Episode {Num} Completed: {Path}", ep.EpisodeNumber, videoPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed rendering Episode {Num}", ep.EpisodeNumber);
                    ep.Status = "Failed";
                    ep.ErrorMessage = ex.Message;
                    await context.SaveChangesAsync(ct);
                }
            }

            project.Status = completedCount == episodes.Count ? "Completed" : "PartialCompleted";
            project.UpdatedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync(ct);

            _logger.LogInformation("================ FILM PRODUCTION COMPLETED: {Comp}/{Total} EPISODES READY ================",
                completedCount, episodes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in Film Production Pipeline for Project {Id}", projectId);
            project.Status = "Failed";
            project.ErrorMessage = ex.Message;
            project.UpdatedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync(ct);
        }
    }
}
