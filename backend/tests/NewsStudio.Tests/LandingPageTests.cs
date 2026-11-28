using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using Xunit;

namespace NewsStudio.Tests;

/// <summary>Public landing page /cheo-uy-tinh: its API is open, everything else stays guarded.</summary>
public class LandingPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LandingPageTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task PublicEndpointsNeedNoAdminKeyWhileTheRestStaysGuarded()
    {
        var guarded = _factory.WithWebHostBuilder(b => b.UseSetting("Security:AdminApiKey", "landing-test-key"));
        var client = guarded.CreateClient();
        var slug = "test-landing-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            // Admin API still refuses an anonymous caller
            (await client.GetAsync("/api/facebook/pages")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            // Public: counters accepted without key
            (await client.PostAsJsonAsync("/api/public/landing/events", new LandingEventRequestDto
            {
                Slug = slug, Type = "view", Device = "mobile", VisitorId = "v1", UtmSource = "zalo", Referrer = "https://m.facebook.com/some/path",
            })).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await client.PostAsJsonAsync("/api/public/landing/events", new LandingEventRequestDto
            {
                Slug = slug, Type = "open_post", Target = "123_456", Device = "desktop", VisitorId = "v1",
            })).StatusCode.Should().Be(HttpStatusCode.NoContent);

            // Junk is refused
            (await client.PostAsJsonAsync("/api/public/landing/events", new LandingEventRequestDto { Slug = slug, Type = "follow" }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await client.PostAsJsonAsync("/api/public/landing/events", new LandingEventRequestDto { Slug = "Bad Slug!", Type = "view" }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // Unknown / not-connected page → 404, never an error page with internals
            (await client.GetAsync("/api/public/landing/pages/99999999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.GetAsync("/api/public/landing/pages/abc")).StatusCode.Should().Be(HttpStatusCode.NotFound);

            // Stats are admin-only…
            (await client.GetAsync($"/api/landing/{slug}/stats")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            // …and count what was recorded
            client.DefaultRequestHeaders.Add("X-Admin-Api-Key", "landing-test-key");
            var stats = await client.GetFromJsonAsync<LandingStatsDto>($"/api/landing/{slug}/stats");
            stats!.Views.Should().Be(1);
            stats.UniqueVisitors.Should().Be(1);
            stats.MobileViews.Should().Be(1);
            stats.PostClicks.Should().Be(1);
            stats.OpenPageClicks.Should().Be(0);
            stats.Sources.Should().ContainSingle(s => s.Key == "zalo" && s.Count == 1);
            stats.TopPosts.Should().ContainSingle(p => p.Key == "123_456");
        }
        finally
        {
            using var scope = guarded.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.LandingEvents.RemoveRange(await db.LandingEvents.Where(e => e.Slug == slug).ToListAsync());
            await db.SaveChangesAsync();
        }
    }
}
