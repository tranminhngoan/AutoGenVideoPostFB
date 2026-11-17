using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Ai;

namespace NewsStudio.Api.Controllers;

/// <summary>Real AI spend this month (from Gemini's token counts) and the monthly budget.</summary>
[ApiController]
[Route("api/ai-usage")]
public class AiUsageController : ControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly AiUsageMeter _meter;

    public AiUsageController(IApplicationDbContext context, AiUsageMeter meter)
    {
        _context = context;
        _meter = meter;
    }

    [HttpGet]
    public async Task<ActionResult<AiUsageReportDto>> Get(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var since = AiUsageMeter.MonthStartUtc(now);
        var rows = await _context.AiUsages.AsNoTracking()
            .Where(u => u.CreatedAtUtc >= since)
            .Select(u => new { u.Kind, u.FreeKey, u.CostUsd, u.CreatedAtUtc, u.VideoProjectId })
            .ToListAsync(cancellationToken);
        var state = await _meter.GetStateAsync(cancellationToken);

        var vnNow = now.AddHours(7);
        var dayOfMonth = vnNow.Day;
        var daysInMonth = DateTime.DaysInMonth(vnNow.Year, vnNow.Month);
        var spent = rows.Sum(r => r.CostUsd);
        var videos = rows.Where(r => r.VideoProjectId != null).Select(r => r.VideoProjectId).Distinct().Count();

        return Ok(new AiUsageReportDto
        {
            Budget = state,
            ProjectedUsd = dayOfMonth > 0 ? Math.Round(spent / dayOfMonth * daysInMonth, 2) : spent,
            ByKind = rows.GroupBy(r => r.Kind)
                .Select(g => new AiUsageKindDto { Kind = g.Key, Calls = g.Count(), FreeCalls = g.Count(r => r.FreeKey), CostUsd = Math.Round(g.Sum(r => r.CostUsd), 4) })
                .OrderByDescending(k => k.CostUsd).ToList(),
            ByDay = rows.GroupBy(r => r.CreatedAtUtc.AddHours(7).ToString("yyyy-MM-dd"))
                .OrderBy(g => g.Key)
                .Select(g => new AiUsageDayDto { Date = g.Key, CostUsd = Math.Round(g.Sum(r => r.CostUsd), 4) }).ToList(),
            Videos = videos,
            // Speech isn't tied to a project id: spread the whole month's spend over the videos made
            CostPerVideo = videos > 0 ? Math.Round(spent / videos, 4) : 0
        });
    }

    [HttpPut("budget")]
    public async Task<ActionResult<AiBudgetStateDto>> SetBudget([FromBody] AiBudgetSettingsDto budget, CancellationToken cancellationToken)
    {
        var value = JsonSerializer.Serialize(AiUsageMeter.Normalize(budget));
        var row = await _context.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == AiUsageMeter.BudgetKey, cancellationToken);
        if (row == null)
        {
            _context.SystemSettings.Add(new SystemSetting { SettingKey = AiUsageMeter.BudgetKey, SettingValue = value, Group = "AI", IsEncrypted = false, Status = "Active" });
        }
        else
        {
            row.SettingValue = value;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync(cancellationToken);
        _meter.Invalidate();
        return Ok(await _meter.GetStateAsync(cancellationToken));
    }
}
