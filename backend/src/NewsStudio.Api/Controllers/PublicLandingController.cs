using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Api.Controllers;

/// <summary>
/// PUBLIC endpoints for the public landing pages (e.g. /cheo-uy-tinh). No admin key, no login:
/// they only return display data a visitor could see on Facebook anyway, and only accept
/// anonymous visit / click counters. Rate-limited per IP (see Program.cs).
/// </summary>
[ApiController]
[Route("api/public/landing")]
[Produces("application/json")]
public partial class PublicLandingController : ControllerBase
{
    private static readonly HashSet<string> EventTypes = new(StringComparer.Ordinal) { "view", "open_page", "open_post" };

    private readonly IFacebookPublishingService _facebook;
    private readonly IApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PublicLandingController> _logger;

    public PublicLandingController(IFacebookPublishingService facebook, IApplicationDbContext db, IMemoryCache cache, ILogger<PublicLandingController> logger)
    {
        _facebook = facebook;
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Name, about, avatar, cover and latest posts of a connected Page (cached 10 min).</summary>
    [HttpGet("pages/{pageId}")]
    public async Task<ActionResult<PageShowcaseDto>> GetPage(string pageId, [FromQuery] int posts = 6, CancellationToken ct = default)
    {
        if (!DigitsOnly().IsMatch(pageId)) return NotFound();
        posts = Math.Clamp(posts, 1, 12);
        var key = $"landing:page:{pageId}:{posts}";
        if (!_cache.TryGetValue(key, out PageShowcaseDto? showcase))
        {
            try
            {
                showcase = await _facebook.GetPageShowcaseAsync(pageId, posts, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Landing page info for {Page} failed: {Message}", pageId, ex.Message);
                return StatusCode(StatusCodes.Status502BadGateway, new { error = "Không đọc được thông tin Page lúc này." });
            }
            _cache.Set(key, showcase, showcase == null ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(10));
        }
        if (showcase == null) return NotFound(new { error = "Page chưa được kết nối." });
        Response.Headers.CacheControl = "public, max-age=300";
        return Ok(showcase);
    }

    /// <summary>Records one anonymous event (view / open_page / open_post). A click is not a follow.</summary>
    [HttpPost("events")]
    public async Task<IActionResult> Track([FromBody] LandingEventRequestDto request, CancellationToken ct)
    {
        if (request == null || !Slug().IsMatch(request.Slug ?? "") || !EventTypes.Contains(request.Type ?? ""))
            return BadRequest(new { error = "Sự kiện không hợp lệ." });

        static string? Clip(string? v, int max)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            var t = v.Trim();
            return t.Length > max ? t[..max] : t;
        }
        string? referrerHost = null;
        if (Uri.TryCreate(request.Referrer, UriKind.Absolute, out var refUri)) referrerHost = Clip(refUri.Host, 200);

        _db.LandingEvents.Add(new LandingEvent
        {
            Slug = request.Slug,
            Type = request.Type,
            Target = request.Type == "open_post" ? Clip(request.Target, 200) : null,
            Device = request.Device == "mobile" ? "mobile" : "desktop",
            VisitorId = Clip(request.VisitorId, 64),
            UtmSource = Clip(request.UtmSource, 100),
            UtmMedium = Clip(request.UtmMedium, 100),
            UtmCampaign = Clip(request.UtmCampaign, 100),
            ReferrerHost = referrerHost,
        });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [GeneratedRegex("^[0-9]{5,30}$")]
    private static partial Regex DigitsOnly();

    [GeneratedRegex("^[a-z0-9-]{1,60}$")]
    private static partial Regex Slug();
}
