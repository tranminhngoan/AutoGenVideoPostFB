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

    // ───────────── AI video studio guard ─────────────
    // No login exists, so anything that spends money or writes data needs X-Studio-Key = the server's xAI / paid Gemini key.
    // The key is set on this shared host only for the test (a second test host would re-freeze Serilog's logger).

    private const string StudioKey = "test-gemini-paid-key";

    private async Task WithStudioKey(Func<Task> test)
    {
        var config = (Microsoft.Extensions.Configuration.IConfiguration)_factory.Services.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration))!;
        var before = config["Ai:Gemini:ApiKey"];
        config["Ai:Gemini:ApiKey"] = StudioKey;
        try { await test(); }
        finally { config["Ai:Gemini:ApiKey"] = before; }
    }

    private static HttpRequestMessage StudioRequest(string method, string url, string body, string? key)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), url);
        if (body.Length > 0) req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        if (key != null) req.Headers.Add("X-Studio-Key", key);
        return req;
    }

    public static IEnumerable<object[]> GuardedStudioCalls() => new[]
    {
        new object[] { "POST", "/api/studio/videos", "{\"title\":\"x\"}" },
        new object[] { "POST", "/api/studio/videos/00000000-0000-0000-0000-000000000001/generate", "" },
        new object[] { "POST", "/api/studio/videos/00000000-0000-0000-0000-000000000001/plan", "" },
        new object[] { "POST", "/api/studio/videos/00000000-0000-0000-0000-000000000001/publish", "{}" },
        new object[] { "DELETE", "/api/studio/videos/00000000-0000-0000-0000-000000000001", "" },
        new object[] { "PUT", "/api/studio/settings/budget", "{\"monthlyBudgetUsd\":1000}" },
        new object[] { "DELETE", "/api/studio/music/00000000-0000-0000-0000-000000000001", "" },
    };

    [Theory]
    [MemberData(nameof(GuardedStudioCalls))]
    public Task Studio_WithoutTheKey_IsRefused(string method, string url, string body) => WithStudioKey(async () =>
    {
        var none = await _client.SendAsync(StudioRequest(method, url, body, null));
        none.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await none.Content.ReadAsStringAsync()).Should().Contain("needsStudioKey");

        var wrong = await _client.SendAsync(StudioRequest(method, url, body, "guess"));
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    });

    [Fact]
    public Task Studio_WithTheKey_PassesTheGuard() => WithStudioKey(async () =>
    {
        // unknown video: past the guard the service answers 404, proving the key was accepted
        var res = await _client.SendAsync(StudioRequest("POST", "/api/studio/videos/00000000-0000-0000-0000-000000000001/plan", "", StudioKey));
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    });

    [Fact]
    public Task Studio_SavingShots_InsertsAndUpdatesThem() => WithStudioKey(async () =>
    {
        // regression: new shots carry their Guid from construction, so attaching them through the video's
        // navigation made EF issue an UPDATE (0 rows → 500) instead of an INSERT
        var created = await _client.SendAsync(StudioRequest("POST", "/api/studio/videos",
            "{\"title\":\"Thử shot\",\"script\":\"Anh Hai: Ê Út Tèo!\",\"targetDurationSeconds\":20}", StudioKey));
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        var saved = await _client.SendAsync(StudioRequest("PUT", $"/api/studio/videos/{id}/shots",
            "[{\"durationSeconds\":8,\"action\":\"He waves\",\"camera\":\"medium shot\",\"speaker\":\"Anh Hai\",\"dialogue\":\"Ê Út Tèo!\",\"assetIds\":[],\"continueFromPrevious\":true}," +
            "{\"durationSeconds\":40,\"action\":\"Fish grilling\",\"camera\":\"close-up\",\"assetIds\":[],\"continueFromPrevious\":true}]", StudioKey));
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        var video = JsonDocument.Parse(await saved.Content.ReadAsStringAsync()).RootElement;
        var shots = video.GetProperty("shots").EnumerateArray().ToList();
        shots.Should().HaveCount(2);
        shots[1].GetProperty("durationSeconds").GetInt32().Should().Be(15); // clamped to the model's max
        shots[0].GetProperty("continueFromPrevious").GetBoolean().Should().BeFalse(); // the first shot has nothing to continue
        shots[1].GetProperty("continueFromPrevious").GetBoolean().Should().BeTrue();
        video.GetProperty("status").GetString().Should().Be("Planned");
        video.GetProperty("targetDurationSeconds").GetInt32().Should().Be(23); // length = the shots, not the number typed

        // saving the details again (with a script estimate) must not overwrite the shots' real length
        (await _client.SendAsync(StudioRequest("PUT", $"/api/studio/videos/{id}", "{\"targetDurationSeconds\":180}", StudioKey)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var reread = JsonDocument.Parse(await _client.GetStringAsync($"/api/studio/videos/{id}")).RootElement;
        reread.GetProperty("targetDurationSeconds").GetInt32().Should().Be(23);

        // edit the first shot (by id), drop the second, add a new one
        var firstId = shots[0].GetProperty("id").GetString();
        var edited = await _client.SendAsync(StudioRequest("PUT", $"/api/studio/videos/{id}/shots",
            $"[{{\"id\":\"{firstId}\",\"durationSeconds\":6,\"action\":\"He waves and laughs\",\"camera\":\"medium shot\",\"assetIds\":[]}}," +
            "{\"durationSeconds\":5,\"action\":\"Glasses clink\",\"camera\":\"close-up\",\"assetIds\":[]}]", StudioKey));
        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = JsonDocument.Parse(await edited.Content.ReadAsStringAsync()).RootElement.GetProperty("shots").EnumerateArray().ToList();
        after.Should().HaveCount(2);
        after[0].GetProperty("id").GetString().Should().Be(firstId);
        after[0].GetProperty("durationSeconds").GetInt32().Should().Be(6);

        (await _client.SendAsync(StudioRequest("DELETE", $"/api/studio/videos/{id}", "", StudioKey))).StatusCode.Should().Be(HttpStatusCode.NoContent);
    });

    [Fact]
    public Task Studio_ReadsStayOpen() => WithStudioKey(async () =>
    {
        (await _client.GetAsync("/api/studio/videos")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync("/api/studio/costs")).StatusCode.Should().Be(HttpStatusCode.OK);
    });
}
