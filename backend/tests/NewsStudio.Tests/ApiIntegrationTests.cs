using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace NewsStudio.Tests;

[Collection("DatabaseCollection")]
public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RootEndpoint_ReturnsHealthyStatusAndDocsUrl()
    {
        // Act
        var response = await _client.GetAsync("/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Trạm Tin 24/7 API");
        content.Should().Contain("/swagger");
    }

    [Fact]
    public async Task StandardHealthEndpoints_ReturnSuccess()
    {
        // Act & Assert /health
        var healthResponse = await _client.GetAsync("/health");
        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act & Assert /health/ready
        var readyResponse = await _client.GetAsync("/health/ready");
        readyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act & Assert /health/live
        var liveResponse = await _client.GetAsync("/health/live");
        liveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApiHealthController_ReturnsDetailedJson()
    {
        // Act
        var response = await _client.GetAsync("/api/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        root.GetProperty("status").GetString().Should().Be("Healthy");
        root.GetProperty("version").GetString().Should().Be("1.0.0");
        root.GetProperty("components").EnumerateObject().Should().NotBeEmpty();
    }

    [Fact]
    public async Task SwaggerEndpoint_ReturnsValidJsonSpec()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Trạm Tin 24/7 API");
    }

    [Fact]
    public async Task SystemConfigSummary_DoesNotExposeSecrets()
    {
        // Act
        var response = await _client.GetAsync("/api/system/config-summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        root.TryGetProperty("ai", out var ai).Should().BeTrue();
        ai.TryGetProperty("apiKey", out _).Should().BeFalse(); // No API key exposed!

        root.TryGetProperty("facebook", out var fb).Should().BeTrue();
        fb.TryGetProperty("pageAccessToken", out _).Should().BeFalse(); // No token exposed!
    }

    // ───────────── GPU control guard ─────────────
    // The API has no login, so endpoints that spend money on the Vast account (rent, start, destroy…) must refuse
    // callers without the account's own Vast API key. The key is set on this shared host for the duration of a test
    // (a second test host would re-freeze Serilog's bootstrap logger). Nothing reaches Vast: the guard answers first.

    private const string VastKey = "test-vast-key-123";

    private async Task WithVastKey(Func<Task> test)
    {
        var config = (Microsoft.Extensions.Configuration.IConfiguration)_factory.Services.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration))!;
        var before = config["Film:Gpu:VastAi:ApiKey"];
        config["Film:Gpu:VastAi:ApiKey"] = VastKey;
        try { await test(); }
        finally { config["Film:Gpu:VastAi:ApiKey"] = before; }
    }

    private static HttpRequestMessage GpuRequest(string method, string url, string body, string? key)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), url);
        if (body.Length > 0) req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        if (key != null) req.Headers.Add("X-Vast-Key", key);
        return req;
    }

    public static IEnumerable<object[]> GuardedGpuCalls() => new[]
    {
        new object[] { "POST", "/api/film-factory/gpu/vast/rent", "{\"offerId\":1,\"diskGb\":200}" },
        new object[] { "DELETE", "/api/film-factory/gpu/vast/instances/123", "" },
        new object[] { "POST", "/api/film-factory/gpu/start", "" },
        new object[] { "POST", "/api/film-factory/gpu/stop", "" },
        new object[] { "POST", "/api/film-factory/gpu/instance", "{\"target\":\"http://attacker.example:8888\"}" },
        new object[] { "GET", "/api/film-factory/gpu/vast/instances", "" },
        new object[] { "GET", "/api/film-factory/gpu/vast/account", "" },
    };

    [Theory]
    [MemberData(nameof(GuardedGpuCalls))]
    public Task GpuControl_WithoutTheVastKey_IsRefused(string method, string url, string body) => WithVastKey(async () =>
    {
        var none = await _client.SendAsync(GpuRequest(method, url, body, null));
        none.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await none.Content.ReadAsStringAsync()).Should().Contain("needsVastKey");

        var wrong = await _client.SendAsync(GpuRequest(method, url, body, "guess"));
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    });

    [Fact]
    public Task GpuControl_WithTheRightKey_PassesTheGuard() => WithVastKey(async () =>
    {
        // invalid offer id: past the guard the controller itself answers 400, proving the key was accepted
        var res = await _client.SendAsync(GpuRequest("POST", "/api/film-factory/gpu/vast/rent", "{\"offerId\":0,\"diskGb\":200}", VastKey));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    });

    [Fact]
    public Task GpuOfferSearch_IsPublic() => WithVastKey(async () =>
    {
        // public market data: validation runs (diskGb out of range -> 400), not the guard (401)
        var res = await _client.GetAsync("/api/film-factory/gpu/vast/offers?diskGb=1");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    });
}
