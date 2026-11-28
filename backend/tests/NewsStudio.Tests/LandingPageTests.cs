using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Facebook;
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

    [Fact]
    public async Task FeaturedPostsNeverIncludeStories()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<IFacebookTokenProtectionService>();
        var pageId = "9" + Random.Shared.NextInt64(100_000_000, 999_999_999);
        var page = new FacebookAccount { PageId = pageId, PageName = "Test Landing", EncryptedAccessToken = tokens.EncryptToken("tok") };
        db.FacebookAccounts.Add(page);
        // A story posted by the daily digest: Facebook's id may come back with or without the page prefix
        var digest = new DailyDigestRun { FacebookAccountId = page.Id, DigestDate = "2026-11-28", StoryPostId = "777", Status = "Done" };
        db.DailyDigestRuns.Add(digest);
        await db.SaveChangesAsync();

        try
        {
            var feed = """
            {"data":[
              {"id":"PAGE_111","message":"Bài thường","full_picture":"https://x/a.jpg","permalink_url":"https://www.facebook.com/PAGE/posts/111","created_time":"2026-11-28T01:00:00+0000"},
              {"id":"PAGE_777","full_picture":"https://x/b.jpg","permalink_url":"https://www.facebook.com/reel/777","created_time":"2026-11-28T02:00:00+0000"},
              {"id":"PAGE_888","full_picture":"https://x/c.jpg","permalink_url":"https://www.facebook.com/stories/PAGE/888","created_time":"2026-11-28T03:00:00+0000"},
              {"id":"PAGE_999","message":"Story qua attachment","full_picture":"https://x/d.jpg","permalink_url":"https://www.facebook.com/PAGE/posts/999","attachments":{"data":[{"media_type":"video","url":"https://www.facebook.com/stories/PAGE/999"}]}}
            ]}
            """.Replace("PAGE", pageId);
            var http = new HttpClient(new FakeGraph(url => url.Contains("/posts?") ? feed : """{"name":"Test Landing"}"""));
            var service = new FacebookPublishingService(db, tokens, scope.ServiceProvider.GetRequiredService<IVideoAssetService>(), http,
                scope.ServiceProvider.GetRequiredService<IConfiguration>(), NullLogger<FacebookPublishingService>.Instance);

            var showcase = await service.GetPageShowcaseAsync(pageId, 6);

            showcase!.Posts.Select(p => p.Id).Should().Equal($"{pageId}_111");
        }
        finally
        {
            db.DailyDigestRuns.Remove(digest);
            db.FacebookAccounts.Remove(page);
            await db.SaveChangesAsync();
        }
    }

    private sealed class FakeGraph(Func<string, string> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body(request.RequestUri!.ToString()), System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
