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
}
