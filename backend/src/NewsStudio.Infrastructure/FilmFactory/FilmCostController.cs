using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Infrastructure.FilmFactory;

public class FilmCostController : IFilmCostController
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<FilmCostController> _logger;

    public FilmCostController(IApplicationDbContext context, ILogger<FilmCostController> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<bool> CanExecutePaidTaskAsync(Guid projectId, decimal estimatedCostUsd, CancellationToken ct = default)
    {
        var project = await _context.FilmProjects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return false;

        var spent = await _context.FilmCostRecords
            .Where(c => c.FilmProjectId == projectId)
            .SumAsync(c => (decimal?)c.CostUsd, ct) ?? 0m;

        if (spent + estimatedCostUsd > project.TotalBudgetLimitUsd)
        {
            _logger.LogWarning("CostController: BLOCKED task costing ${Est:F4} for Project {Id}. Current spent: ${Spent:F4}, Limit: ${Limit:F4}",
                estimatedCostUsd, projectId, spent, project.TotalBudgetLimitUsd);
            return false;
        }

        return true;
    }

    public async Task RecordCostAsync(
        Guid projectId,
        Guid? episodeId,
        Guid? shotId,
        string provider,
        string serviceType,
        long units,
        decimal costUsd,
        bool isFree,
        string? note = null,
        CancellationToken ct = default)
    {
        try
        {
            var record = new FilmCostRecord
            {
                FilmProjectId = projectId,
                FilmEpisodeId = episodeId,
                FilmShotId = shotId,
                Provider = provider,
                ServiceType = serviceType,
                QuantityUnits = units,
                CostUsd = Math.Round(costUsd, 4),
                IsFreeTier = isFree,
                Note = note
            };

            _context.FilmCostRecords.Add(record);

            var project = await _context.FilmProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
            if (project != null)
            {
                project.TotalActualCostUsd += record.CostUsd;
                project.UpdatedAtUtc = DateTime.UtcNow;
            }

            if (episodeId.HasValue)
            {
                var episode = await _context.FilmEpisodes.FirstOrDefaultAsync(e => e.Id == episodeId.Value, ct);
                if (episode != null)
                {
                    episode.ActualCostUsd += record.CostUsd;
                    episode.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            if (shotId.HasValue)
            {
                var shot = await _context.FilmShots.FirstOrDefaultAsync(s => s.Id == shotId.Value, ct);
                if (shot != null)
                {
                    shot.ActualCostUsd += record.CostUsd;
                    shot.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Recorded Film Cost: ${Cost:F4} ({Provider}/{ServiceType}) for Project {ProjId}",
                costUsd, provider, serviceType, projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record film cost for project {Id}", projectId);
        }
    }

    public async Task<FilmCostSummaryDto> EvaluateBudgetStatusAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _context.FilmProjects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        var limit = project?.TotalBudgetLimitUsd ?? 5.0m;

        var records = await _context.FilmCostRecords
            .Where(c => c.FilmProjectId == projectId)
            .ToListAsync(ct);

        var spent = records.Sum(r => r.CostUsd);
        var remaining = Math.Max(0m, limit - spent);

        string status;
        if (spent >= limit)
        {
            status = "BUDGET_EXHAUSTED";
        }
        else if (spent >= limit * 0.8m)
        {
            status = "BUDGET_WARNING";
        }
        else if (spent > 0m)
        {
            status = "LOW_COST";
        }
        else
        {
            status = "FREE_ONLY";
        }

        var shots = await _context.FilmShots
            .Where(s => s.FilmEpisode != null && s.FilmEpisode.FilmProjectId == projectId)
            .ToListAsync(ct);

        return new FilmCostSummaryDto
        {
            TotalBudgetLimitUsd = limit,
            TotalSpentUsd = Math.Round(spent, 4),
            RemainingBudgetUsd = Math.Round(remaining, 4),
            BudgetStatus = status,
            ScriptCostUsd = Math.Round(records.Where(r => r.ServiceType == "ScriptLLM").Sum(r => r.CostUsd), 4),
            VisualCostUsd = Math.Round(records.Where(r => r.ServiceType == "ImageGen" || r.ServiceType == "VideoGen").Sum(r => r.CostUsd), 4),
            VoiceCostUsd = Math.Round(records.Where(r => r.ServiceType == "VoiceTTS").Sum(r => r.CostUsd), 4),
            RenderCostUsd = Math.Round(records.Where(r => r.ServiceType == "CloudCompute").Sum(r => r.CostUsd), 4),
            TotalAiVideoShots = shots.Count(s => s.VisualMethod == "AiVideo"),
            TotalMotionImageShots = shots.Count(s => s.VisualMethod == "CinematicMotionImage"),
            TotalFreeApiCalls = records.Count(r => r.IsFreeTier),
            TotalPaidApiCalls = records.Count(r => !r.IsFreeTier)
        };
    }
}
