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

    // Paid AI calls inside: a failed run is reported on the project, never silently retried.
    [Queue("render")]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteProductionPipelineAsync(Guid projectId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var showrunner = scope.ServiceProvider.GetRequiredService<IFilmShowrunnerService>();
        var renderer = scope.ServiceProvider.GetRequiredService<IFilmRendererService>();

        var project = await context.FilmProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null)
        {
            _logger.LogError("Production Pipeline: Film project {Id} not found.", projectId);
            return;
        }

        if (project.Status == "InProduction")
        {
            _logger.LogWarning("Production Pipeline: Film project {Id} is already InProduction. Skipping duplicate pipeline run.", projectId);
            return;
        }

        try
        {
            _logger.LogInformation("=== FILM PRODUCTION: {Title} ({Id}) ===", project.Title, project.Id);
            project.ErrorMessage = null;

            _logger.LogInformation(">>> 1/4 Story Bible");
            await showrunner.DevelopStoryBibleAsync(projectId, ct);

            _logger.LogInformation(">>> 2/4 Characters & locations");
            await showrunner.GenerateCharactersAndLocationsAsync(projectId, ct);

            var episodes = await context.FilmEpisodes
                .Where(e => e.FilmProjectId == projectId)
                .OrderBy(e => e.EpisodeNumber)
                .ToListAsync(ct);

            project.Status = "InProduction";
            project.UpdatedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync(ct);

            _logger.LogInformation(">>> 3/4 Scripts for {Count} episodes", episodes.Count);
            var scripted = new HashSet<Guid>();
            foreach (var ep in episodes)
            {
                // One refused or broken episode must not stop the other nine; it is marked Failed with the reason.
                try
                {
                    using var epScope = _scopeFactory.CreateScope();
                    var epShowrunner = epScope.ServiceProvider.GetRequiredService<IFilmShowrunnerService>();
                    await epShowrunner.WriteEpisodeScriptAsync(ep.Id, ct);
                    await epShowrunner.CheckContinuityAsync(ep.Id, ct);
                    scripted.Add(ep.Id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Script failed for Episode {Num}", ep.EpisodeNumber);
                    var reason = "Viết kịch bản thất bại: " + ex.Message;
                    await MarkEpisodeFailedAsync(ep.Id, reason);
                    ep.Status = "Failed"; // keep this context's copy in step so a later save can't revert it
                    ep.ErrorMessage = reason;
                }
            }

            _logger.LogInformation(">>> 4/4 Rendering {Count} episodes", scripted.Count);
            var gpu = scope.ServiceProvider.GetRequiredService<FilmGpuService>();
            var completed = 0;
            foreach (var ep in episodes.Where(e => scripted.Contains(e.Id)))
            {
                try
                {
                    if (gpu.Enabled)
                    {
                        // Free-GPU path: voices now, then the episode waits its turn on the GPU worker;
                        // the recurring GPU tick assembles it when its frames and clips arrive.
                        await renderer.PrepareVoicesAsync(ep.Id, ct);
                        await SetEpisodeStatusAsync(ep.Id, FilmGpuService.StatusQueued);
                        completed++;
                        continue;
                    }
                    await renderer.AssembleEpisodeAsync(ep.Id, ct);
                    completed++;
                }
                catch (Exception ex)
                {
                    // The renderer already stored the reason on the episode. A budget stop ends the run.
                    _logger.LogError(ex, "Failed rendering Episode {Num}", ep.EpisodeNumber);
                    if (ex.Message.Contains("ngân sách", StringComparison.OrdinalIgnoreCase))
                    {
                        project.ErrorMessage = ex.Message;
                        break;
                    }
                }
            }

            var finalStatus = completed < episodes.Count ? "PartialCompleted" : gpu.Enabled ? "InProduction" : "Completed";
            await SetProjectStatusAsync(projectId, finalStatus, project.ErrorMessage);
            _logger.LogInformation("=== FILM PRODUCTION DONE: {Done}/{Total} episodes ===", completed, episodes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in Film Production Pipeline for Project {Id}", projectId);
            await SetProjectStatusAsync(projectId, "Failed", ex.Message);
        }
    }

    public async Task TriggerEpisodeRenderAsync(Guid episodeId, CancellationToken ct = default)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var ep = await context.FilmEpisodes.FirstOrDefaultAsync(e => e.Id == episodeId, ct)
                     ?? throw new ArgumentException("Không tìm thấy tập phim.");
            // A busy status that has not moved for a while belongs to a job that died (e.g. server restart).
            // GPU batches legitimately run for hours, so they get a much longer allowance.
            var gpuBusy = ep.Status is FilmGpuService.StatusQueued or FilmGpuService.StatusRendering or "GpuDone";
            var busy = gpuBusy || ep.Status is "Queued" or "Scripting" or "VisualAssetsGenerating" or "VoiceGenerating" or "FilmEditing" or "QualityCheck";
            if (busy && ep.UpdatedAtUtc > DateTime.UtcNow.AddMinutes(gpuBusy ? -12 * 60 : -15))
                throw new InvalidOperationException($"Tập {ep.EpisodeNumber} đang được dựng ({ep.Status}).");
            ep.Status = "Queued";
            ep.ErrorMessage = null;
            ep.UpdatedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync(ct);
        }
        _jobClient.Enqueue<IFilmProductionCoordinator>(c => c.RenderEpisodeJobAsync(episodeId, CancellationToken.None));
    }

    [Queue("render")]
    [AutomaticRetry(Attempts = 0)]
    public async Task RenderEpisodeJobAsync(Guid episodeId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var showrunner = scope.ServiceProvider.GetRequiredService<IFilmShowrunnerService>();
        var renderer = scope.ServiceProvider.GetRequiredService<IFilmRendererService>();

        var episode = await context.FilmEpisodes
            .Include(e => e.FilmProject).ThenInclude(p => p!.Characters)
            .Include(e => e.Scenes).ThenInclude(s => s.Shots)
            .FirstOrDefaultAsync(e => e.Id == episodeId, ct);
        if (episode == null)
        {
            _logger.LogError("Render job: episode {Id} not found.", episodeId);
            return;
        }

        try
        {
            // Projects made before the AI cast/screenwriter existed carry template characters and scripts
            // that have nothing to do with their story: replace them before rendering.
            if (FilmShowrunnerService.NeedsCasting(episode.FilmProject!))
            {
                _logger.LogInformation("Render job: casting project {Id} from its story bible", episode.FilmProjectId);
                await showrunner.GenerateCharactersAndLocationsAsync(episode.FilmProjectId, ct);
            }
            if (FilmShowrunnerService.NeedsScript(episode))
            {
                _logger.LogInformation("Render job: writing script for episode {Num}", episode.EpisodeNumber);
                await showrunner.WriteEpisodeScriptAsync(episode.Id, ct);
                await showrunner.CheckContinuityAsync(episode.Id, ct);
            }

            var gpu = scope.ServiceProvider.GetRequiredService<FilmGpuService>();
            var reloaded = await context.FilmEpisodes.AsNoTracking()
                .Include(e => e.Scenes).ThenInclude(s => s.Shots)
                .FirstAsync(e => e.Id == episode.Id, ct);
            var shots = reloaded.Scenes.SelectMany(s => s.Shots).ToList();
            var assetsReady = shots.All(s => !string.IsNullOrWhiteSpace(s.RawImagePath) && System.IO.File.Exists(s.RawImagePath))
                              && shots.Where(s => s.UseMotion).All(s => !string.IsNullOrWhiteSpace(s.MotionVideoPath) && System.IO.File.Exists(s.MotionVideoPath));
            if (gpu.Enabled && !assetsReady)
            {
                await renderer.PrepareVoicesAsync(episode.Id, ct);
                await SetEpisodeStatusAsync(episode.Id, FilmGpuService.StatusQueued);
                _logger.LogInformation("Episode {Num} queued for the GPU worker", episode.EpisodeNumber);
                return;
            }

            await renderer.AssembleEpisodeAsync(episode.Id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Render job failed for episode {Id}", episodeId);
            await MarkEpisodeFailedAsync(episodeId, ex.Message);
        }
    }

    // Failure states are written through a fresh scope: the job's own context may hold the change that failed.
    private async Task SetProjectStatusAsync(Guid projectId, string status, string? error)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var project = await context.FilmProjects.FirstOrDefaultAsync(p => p.Id == projectId);
        if (project == null) return;
        project.Status = status;
        project.ErrorMessage = error;
        project.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }

    private async Task SetEpisodeStatusAsync(Guid episodeId, string status)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var episode = await context.FilmEpisodes.FirstOrDefaultAsync(e => e.Id == episodeId);
        if (episode == null) return;
        episode.Status = status;
        episode.ErrorMessage = null;
        episode.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }

    private async Task MarkEpisodeFailedAsync(Guid episodeId, string message)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var episode = await context.FilmEpisodes.FirstOrDefaultAsync(e => e.Id == episodeId);
        if (episode == null) return;
        episode.Status = "Failed";
        episode.ErrorMessage = message.Length > 1900 ? message[..1900] : message;
        episode.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }
}
