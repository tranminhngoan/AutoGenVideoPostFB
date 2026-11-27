using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
            "[{\"durationSeconds\":8,\"action\":\"He waves\",\"camera\":\"medium shot\",\"speaker\":\"Anh Hai\",\"dialogue\":\"Ê Út Tèo!\",\"cast\":[\"Anh Hai\",\"Anh Hai\"],\"location\":\"Sân\"}," +
            "{\"durationSeconds\":40,\"action\":\"Fish grilling\",\"camera\":\"close-up\",\"cast\":[]}]", StudioKey));
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        var video = JsonDocument.Parse(await saved.Content.ReadAsStringAsync()).RootElement;
        var shots = video.GetProperty("shots").EnumerateArray().ToList();
        shots.Should().HaveCount(2);
        shots[1].GetProperty("durationSeconds").GetInt32().Should().Be(8); // clamped to Veo's max
        shots[0].GetProperty("cast").EnumerateArray().Select(c => c.GetString()).Should().Equal("Anh Hai"); // duplicates dropped
        shots[0].GetProperty("location").GetString().Should().Be("Sân");
        video.GetProperty("status").GetString().Should().Be("Planned");
        video.GetProperty("targetDurationSeconds").GetInt32().Should().Be(16); // length = the shots (8 + 8), not the number typed

        // saving the details again (with a script estimate) must not overwrite the shots' real length
        (await _client.SendAsync(StudioRequest("PUT", $"/api/studio/videos/{id}", "{\"targetDurationSeconds\":180}", StudioKey)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var reread = JsonDocument.Parse(await _client.GetStringAsync($"/api/studio/videos/{id}")).RootElement;
        reread.GetProperty("targetDurationSeconds").GetInt32().Should().Be(16);

        // edit the first shot (by id), drop the second, add a new one
        var firstId = shots[0].GetProperty("id").GetString();
        var edited = await _client.SendAsync(StudioRequest("PUT", $"/api/studio/videos/{id}/shots",
            $"[{{\"id\":\"{firstId}\",\"durationSeconds\":6,\"action\":\"He waves and laughs\",\"camera\":\"medium shot\",\"cast\":[]}}," +
            "{\"durationSeconds\":5,\"action\":\"Glasses clink\",\"camera\":\"close-up\",\"cast\":[]}]", StudioKey));
        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = JsonDocument.Parse(await edited.Content.ReadAsStringAsync()).RootElement.GetProperty("shots").EnumerateArray().ToList();
        after.Should().HaveCount(2);
        after[0].GetProperty("id").GetString().Should().Be(firstId);
        after[0].GetProperty("durationSeconds").GetInt32().Should().Be(6);

        (await _client.SendAsync(StudioRequest("DELETE", $"/api/studio/videos/{id}", "", StudioKey))).StatusCode.Should().Be(HttpStatusCode.NoContent);
    });

    [Fact]
    public async Task Studio_ClipsLiveOnDisk_AndALostClipIsMarkedToRedo()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NewsStudio.Application.Common.Interfaces.IApplicationDbContext>();
        var files = scope.ServiceProvider.GetRequiredService<NewsStudio.Infrastructure.Studio.StudioFileStore>();

        // two clips kept on disk (empty Data in the database): one still there, one wiped by a redeploy
        var kept = new NewsStudio.Domain.Entities.StudioBlob { MimeType = "video/mp4", SizeBytes = 4 };
        var lost = new NewsStudio.Domain.Entities.StudioBlob { MimeType = "video/mp4", SizeBytes = 4 };
        await files.SaveAsync(kept.Id, new byte[] { 1, 2, 3, 4 }, CancellationToken.None);
        var video = new NewsStudio.Domain.Entities.StudioVideo { Title = "disk test", Status = "Ready", FinalBlobId = lost.Id };
        var s1 = new NewsStudio.Domain.Entities.StudioShot { StudioVideoId = video.Id, Index = 1, Status = "Done", ClipBlobId = kept.Id, Action = "a" };
        var s2 = new NewsStudio.Domain.Entities.StudioShot { StudioVideoId = video.Id, Index = 2, Status = "Done", ClipBlobId = lost.Id, Action = "b" };
        db.StudioBlobs.Add(kept); db.StudioBlobs.Add(lost);
        db.StudioVideos.Add(video); db.StudioShots.Add(s1); db.StudioShots.Add(s2);
        await db.SaveChangesAsync(CancellationToken.None);

        var clip = await _client.GetAsync($"/api/studio/shots/{s1.Id}/clip");
        clip.StatusCode.Should().Be(HttpStatusCode.OK);
        (await clip.Content.ReadAsByteArrayAsync()).Should().Equal(1, 2, 3, 4); // streamed from disk

        var dto = JsonDocument.Parse(await _client.GetStringAsync($"/api/studio/videos/{video.Id}")).RootElement;
        var shots = dto.GetProperty("shots").EnumerateArray().ToList();
        shots[0].GetProperty("hasClip").GetBoolean().Should().BeTrue();
        shots[1].GetProperty("hasClip").GetBoolean().Should().BeFalse();       // lost → to redo, not a broken player
        shots[1].GetProperty("status").GetString().Should().Be("Pending");
        dto.GetProperty("hasFinal").GetBoolean().Should().BeFalse();
        dto.GetProperty("status").GetString().Should().Be("Planned");

        files.Delete(kept.Id);
    }

    [Fact]
    public async Task Studio_AVideoStuckComposingAfterAnOutage_IsReleased()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NewsStudio.Application.Common.Interfaces.IApplicationDbContext>();
        var clip = new NewsStudio.Domain.Entities.StudioBlob { MimeType = "video/mp4", Data = new byte[] { 1 }, SizeBytes = 1 };
        var video = new NewsStudio.Domain.Entities.StudioVideo { Title = "stuck", Status = "Composing" };
        db.StudioBlobs.Add(clip);
        db.StudioVideos.Add(video);
        db.StudioShots.Add(new NewsStudio.Domain.Entities.StudioShot { StudioVideoId = video.Id, Index = 1, Status = "Done", ClipBlobId = clip.Id, Action = "a" });
        await db.SaveChangesAsync(CancellationToken.None);
        // the context stamps CreatedAtUtc on save: age the row directly, as if the outage was two hours ago
        await ((Microsoft.EntityFrameworkCore.DbContext)db).Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"StudioVideos\" SET \"CreatedAtUtc\" = {DateTime.UtcNow.AddHours(-2)}, \"UpdatedAtUtc\" = NULL WHERE \"Id\" = {video.Id}");

        var dto = JsonDocument.Parse(await _client.GetStringAsync($"/api/studio/videos/{video.Id}")).RootElement;
        dto.GetProperty("status").GetString().Should().Be("Planned"); // unlocked: "Ghép lại" can be pressed again
        dto.GetProperty("errorMessage").GetString().Should().Contain("Ghép lại");
    }

    [Fact]
    public Task Studio_ImportsALibraryScriptShotForShot() => WithStudioKey(async () =>
    {
        var body = """
            {"title":"Nồi Cơm Độn Sắn","code":"BCN-01","mood":"Nghẹn ngào rớt nước mắt","region":"Bắc Bộ","era":"8x",
             "hook":"Có những đứa trẻ lớn lên bằng nồi cơm độn sắn.","ending":"Con tìm đâu lại được hạt cơm năm xưa.",
             "scenes":[{"title":"Khói bếp","durationSeconds":15,"camera":"wide establishing shot","promptEn":"A mud-brick kitchen in the rain.",
                        "voiceover":"Những năm chín mươi ấy, nhà nào ở quê cũng nghèo.","sound":"Tiếng mưa"},
                       {"title":"Bát cơm","durationSeconds":30,"camera":"medium close-up","promptEn":"The mother gives her son the rice.",
                        "voiceover":"Mẹ gạt hết hạt cơm trắng sang bát con.","sound":"Tiếng thở dài"}],
             "resolution":"480p","musicMode":"None","autoGenerate":false}
            """;
        var res = await _client.SendAsync(StudioRequest("POST", "/api/studio/videos/import", body, StudioKey));
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var video = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        video.GetProperty("title").GetString().Should().Be("Nồi Cơm Độn Sắn (BCN-01)");
        video.GetProperty("resolution").GetString().Should().Be("720p"); // Veo has no 480p
        var narrator = video.GetProperty("characters").EnumerateArray().Single();
        narrator.GetProperty("name").GetString().Should().Be("Lời dẫn");
        narrator.GetProperty("voice").GetString().Should().Be("gemini:Sulafat");
        narrator.GetProperty("speed").GetString().Should().Be("-8%");
        var shots = video.GetProperty("shots").EnumerateArray().ToList();
        shots.Count.Should().BeGreaterThanOrEqualTo(4); // 15 s + 30 s of scenes → several clips
        shots[0].GetProperty("dialogue").GetString().Should().StartWith("Có những đứa trẻ");
        shots[0].GetProperty("speaker").GetString().Should().Be("Lời dẫn");
        shots.Should().OnlyContain(s => s.GetProperty("durationSeconds").GetInt32() <= 12);

        var missing = await _client.SendAsync(StudioRequest("POST", "/api/studio/videos/import", "{\"title\":\"x\",\"scenes\":[]}", StudioKey));
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.SendAsync(StudioRequest("POST", "/api/studio/videos/import", body, null))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _client.SendAsync(StudioRequest("DELETE", $"/api/studio/videos/{video.GetProperty("id").GetString()}", "", StudioKey));
    });

    [Fact]
    public Task Studio_ReadsStayOpen() => WithStudioKey(async () =>
    {
        (await _client.GetAsync("/api/studio/videos")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync("/api/studio/costs")).StatusCode.Should().Be(HttpStatusCode.OK);
    });
}
