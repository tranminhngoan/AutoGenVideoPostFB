using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;

namespace NewsStudio.Api.Controllers;

/// <summary>Admin view of a public landing page's anonymous counters (behind the normal admin guard).</summary>
[ApiController]
[Route("api/landing")]
[Produces("application/json")]
public class LandingStatsController : ControllerBase
{
    private readonly IApplicationDbContext _db;

    public LandingStatsController(IApplicationDbContext db) => _db = db;

    [HttpGet("{slug}/stats")]
    public async Task<ActionResult<LandingStatsDto>> GetStats(string slug, [FromQuery] int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365);
        var since = DateTime.UtcNow.AddDays(-days);
        var q = _db.LandingEvents.AsNoTracking().Where(e => e.Slug == slug && e.CreatedAtUtc >= since);
        var views = q.Where(e => e.Type == "view");

        return Ok(new LandingStatsDto
        {
            Slug = slug,
            Days = days,
            Views = await views.CountAsync(ct),
            UniqueVisitors = await views.Where(e => e.VisitorId != null).Select(e => e.VisitorId).Distinct().CountAsync(ct),
            OpenPageClicks = await q.CountAsync(e => e.Type == "open_page", ct),
            PostClicks = await q.CountAsync(e => e.Type == "open_post", ct),
            MobileViews = await views.CountAsync(e => e.Device == "mobile", ct),
            DesktopViews = await views.CountAsync(e => e.Device != "mobile", ct),
            Sources = await views
                .GroupBy(e => e.UtmSource ?? e.ReferrerHost ?? "(trực tiếp)")
                .Select(g => new LandingCountDto { Key = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).Take(10).ToListAsync(ct),
            TopPosts = await q.Where(e => e.Type == "open_post" && e.Target != null)
                .GroupBy(e => e.Target!)
                .Select(g => new LandingCountDto { Key = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).Take(10).ToListAsync(ct),
        });
    }

    /// <summary>
    /// "Chéo lại": every click on "Mở Facebook – ủng hộ Page", newest first, so the owner can match the time
    /// with the Page's follow notifications and interact back. status = waiting | done | all.
    /// </summary>
    [HttpGet("{slug}/clicks")]
    public async Task<ActionResult<List<LandingClickDto>>> GetClicks(string slug, [FromQuery] string status = "all", [FromQuery] int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365);
        var since = DateTime.UtcNow.AddDays(-days);
        var q = _db.LandingEvents.AsNoTracking().Where(e => e.Slug == slug && e.Type == "open_page" && e.CreatedAtUtc >= since);
        if (status == "waiting") q = q.Where(e => e.HandledAtUtc == null);
        else if (status == "done") q = q.Where(e => e.HandledAtUtc != null);
        var clicks = await q.OrderByDescending(e => e.CreatedAtUtc).Take(500).ToListAsync(ct);

        var visitors = clicks.Where(e => e.VisitorId != null).Select(e => e.VisitorId!).Distinct().ToList();
        var perVisitor = await _db.LandingEvents.AsNoTracking()
            .Where(e => e.Slug == slug && e.Type == "open_page" && e.VisitorId != null && visitors.Contains(e.VisitorId))
            .GroupBy(e => e.VisitorId!)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return Ok(clicks.Select(e => new LandingClickDto
        {
            Id = e.Id,
            ClickedAtUtc = e.CreatedAtUtc,
            Device = e.Device,
            Source = e.UtmSource ?? e.ReferrerHost,
            Visitor = e.VisitorId is { Length: > 0 } v ? v.Replace("-", "")[..Math.Min(6, v.Replace("-", "").Length)].ToUpperInvariant() : null,
            VisitorClicks = e.VisitorId != null && perVisitor.TryGetValue(e.VisitorId, out var n) ? n : 1,
            HandledAtUtc = e.HandledAtUtc,
        }).ToList());
    }

    /// <summary>Marks one click as "đã trả lại" (interacted back) or back to waiting.</summary>
    [HttpPut("clicks/{id:guid}/handled")]
    public async Task<IActionResult> SetHandled(Guid id, [FromBody] LandingClickHandledDto body, CancellationToken ct)
    {
        var click = await _db.LandingEvents.FirstOrDefaultAsync(e => e.Id == id && e.Type == "open_page", ct);
        if (click == null) return NotFound();
        click.HandledAtUtc = body?.Handled == true ? DateTime.UtcNow : null;
        click.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
