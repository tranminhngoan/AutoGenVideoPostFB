using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Growth;
using Xunit;

namespace NewsStudio.Tests;

// ═══════════════════════════════ pure logic ═══════════════════════════════

public class GrowthLogicTests
{
    [Fact]
    public void VietnamSlotsAreConvertedToUtc()
    {
        GrowthTime.SlotUtc(new DateOnly(2026, 11, 26), "07:30").Should().Be(new DateTime(2026, 11, 26, 0, 30, 0, DateTimeKind.Utc));
        GrowthTime.SlotUtc(new DateOnly(2026, 11, 26), "03:00").Should().Be(new DateTime(2026, 11, 25, 20, 0, 0, DateTimeKind.Utc));
        GrowthTime.DayStartUtc(new DateOnly(2026, 11, 26)).Should().Be(new DateTime(2026, 11, 25, 17, 0, 0, DateTimeKind.Utc));
        GrowthTime.EndUtc(new DateOnly(2026, 11, 26)).Should().Be(new DateTime(2026, 11, 26, 17, 0, 0, DateTimeKind.Utc));
        GrowthTime.Today(new DateTime(2026, 11, 26, 18, 0, 0, DateTimeKind.Utc)).Should().Be(new DateOnly(2026, 11, 27));
    }

    [Fact]
    public void SlotsAreValidatedSortedAndDeduplicated()
    {
        GrowthTime.NormalizeSlots(new[] { "20:00", "7:30", "07:30", " 11:15 " }).Should().Equal("07:30", "11:15", "20:00");
        var bad = () => GrowthTime.NormalizeSlots(new[] { "25:00" });
        bad.Should().Throw<ArgumentException>();
        GrowthTime.ParseSlots("07:30,xx,20:00").Should().Equal("07:30", "20:00");
    }

    [Fact]
    public void CampaignDayNumberCountsFromOne()
    {
        var start = new DateOnly(2026, 12, 1);
        var end = start.AddDays(6);
        GrowthTime.DayNumber(start, end, start.AddDays(-1)).Should().Be(0);
        GrowthTime.DayNumber(start, end, start).Should().Be(1);
        GrowthTime.DayNumber(start, end, end).Should().Be(7);
        GrowthTime.DayNumber(start, end, end.AddDays(3)).Should().Be(8);
    }

    [Fact]
    public void DefaultPlanHasSevenDaysEndingWithTheReport()
    {
        var plan = GrowthCampaignService.DefaultPlan(new DateOnly(2026, 12, 1), 7);
        plan.Should().HaveCount(7);
        plan[0].Focus.Should().Contain("ban đầu");
        plan[^1].Tasks.Should().Contain(t => t.Contains("báo cáo"));
        GrowthCampaignService.DefaultPlan(new DateOnly(2026, 12, 1), 10)[^1].Focus.Should().Be("Tổng kết");
    }

    [Theory]
    [InlineData("Meta báo lỗi: Error validating access token: Session has expired on Monday", GrowthErrorKind.Token)]
    [InlineData("Đăng bài viết thất bại. Meta báo lỗi: (#200) The user hasn't authorized the application", GrowthErrorKind.Permission)]
    [InlineData("Meta báo lỗi: (#10) Application does not have permission for this action", GrowthErrorKind.Permission)]
    [InlineData("Meta báo lỗi: (#4) Application request limit reached", GrowthErrorKind.Transient)]
    [InlineData("Meta báo lỗi: An unexpected error has occurred. Please retry your request later. (#2)", GrowthErrorKind.Transient)]
    [InlineData("Không kết nối được tới Meta Graph API: timeout", GrowthErrorKind.Transient)]
    [InlineData("Something odd", GrowthErrorKind.Unknown)]
    public void PublishErrorsAreClassified(string message, string kind) =>
        GrowthGraphClient.ClassifyMessage(new InvalidOperationException(message)).Should().Be(kind);

    [Fact]
    public void NetworkErrorsAreTransientAndGraphCodesAreMapped()
    {
        GrowthGraphClient.ClassifyMessage(new HttpRequestException("reset")).Should().Be(GrowthErrorKind.Transient);
        GrowthGraphClient.ClassifyMessage(new TaskCanceledException()).Should().Be(GrowthErrorKind.Transient);
        GrowthGraphClient.Classify(190, HttpStatusCode.BadRequest).Should().Be(GrowthErrorKind.Token);
        GrowthGraphClient.Classify(200, HttpStatusCode.Forbidden).Should().Be(GrowthErrorKind.Permission);
        GrowthGraphClient.Classify(null, HttpStatusCode.BadGateway).Should().Be(GrowthErrorKind.Transient);
        GrowthGraphClient.Classify(100, HttpStatusCode.BadRequest).Should().Be(GrowthErrorKind.Content);
        GrowthGraphClient.ParseError("{\"error\":{\"message\":\"Invalid OAuth access token.\",\"code\":190}}", HttpStatusCode.BadRequest)
            .Should().Be(((int?)190, "Invalid OAuth access token."));
    }

    [Fact]
    public void InstagramCaptionsKeepAtMost30HashtagsAnd2200Characters()
    {
        var many = "Chiều quê " + string.Join(" ", Enumerable.Range(1, 40).Select(i => "#tag" + i));
        var ig = GrowthGraphClient.InstagramCaption(many);
        System.Text.RegularExpressions.Regex.Matches(ig, "#tag").Count.Should().Be(30);
        ig.Should().StartWith("Chiều quê #tag1");
        GrowthGraphClient.InstagramCaption(new string('a', 3000)).Length.Should().Be(2200);
    }

    [Fact]
    public void TokensAreNeverKeptInErrorMessages()
    {
        // Meta echoes a malformed token back in its error text
        GrowthGraphClient.Redact("Meta báo lỗi (#190) Malformed access token EAAB_test_page_access_token_secure_987654")
            .Should().Be("Meta báo lỗi (#190) Malformed access token EAA***");
        GrowthGraphClient.Redact("GET https://graph.facebook.com/v21.0/me?access_token=abc123secret&x=1").Should().NotContain("abc123secret");
        GrowthGraphClient.Redact("lỗi với my-plain-token-value", "my-plain-token-value").Should().Be("lỗi với ***");
        GrowthGraphClient.Redact(null).Should().BeEmpty();
    }

    [Fact]
    public void RetryWaitsGrowAndAreCapped()
    {
        GrowthPublisher.Backoff(1).Should().Be(TimeSpan.FromMinutes(2));
        GrowthPublisher.Backoff(2).Should().Be(TimeSpan.FromMinutes(4));
        GrowthPublisher.Backoff(3).Should().Be(TimeSpan.FromMinutes(8));
        GrowthPublisher.Backoff(10).Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void HashtagsAreAddedOnceWithoutRepeatingTheCaption()
    {
        GrowthPublishGateway.ComposeMessage("Tuổi thơ ùa về #tuoitho", "#tuoitho, kyniem #KyNiem tet")
            .Should().Be("Tuổi thơ ùa về #tuoitho\n\n#kyniem #tet");
        GrowthPublishGateway.ComposeMessage("Chỉ chữ", "").Should().Be("Chỉ chữ");
        GrowthContentService.CountHashtags("a #x #y", "#x #z").Should().Be(3);
    }

    [Fact]
    public void KpiFormulasNeedValidData()
    {
        GrowthInsightsService.GrowthPercent(50, 1000).Should().Be(5);
        GrowthInsightsService.GrowthPercent(50, 0).Should().BeNull();
        GrowthInsightsService.GrowthPercent(null, 1000).Should().BeNull();
        GrowthInsightsService.ConversionPercent(30, 600).Should().Be(5);
        GrowthInsightsService.ConversionPercent(30, null).Should().BeNull();
        GrowthInsightsService.ConversionPercent(30, 0).Should().BeNull();
        GrowthInsightsService.SumOrNull(new long?[] { null, null }).Should().BeNull();
        GrowthInsightsService.SumOrNull(new long?[] { 2, null, 3 }).Should().Be(5);
    }

    [Fact]
    public void DailyGainsComeFromLifetimeSnapshots()
    {
        var d = new DateOnly(2026, 12, 1);
        var post = new List<GrowthPostMetric>
        {
            new() { Date = d, Views = 100 }, new() { Date = d.AddDays(1), Views = 250 }, new() { Date = d.AddDays(2), Views = 240 } // a lower reading never subtracts
        };
        var gains = GrowthInsightsService.DailyGains(new[] { post }, m => m.Views);
        gains[d].Should().Be(100);
        gains[d.AddDays(1)].Should().Be(150);
        gains[d.AddDays(2)].Should().Be(0);
    }

    [Fact]
    public void BestHoursNeedEnoughSamplesAndComeFromData()
    {
        var day = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        var few = new List<(DateTime, double, double?)> { (day.AddHours(13), 500, 10), (day.AddHours(13), 600, 12) };
        var notEnough = GrowthInsightsService.ComputeHours(few, 2, "test");
        notEnough.EnoughData.Should().BeFalse();
        notEnough.SuggestedSlots.Should().BeEmpty();

        var many = new List<(DateTime, double, double?)>();
        for (var i = 0; i < 4; i++) many.Add((day.AddHours(13), 900, 20)); // 20:00 Vietnam
        for (var i = 0; i < 4; i++) many.Add((day.AddHours(1), 100, 2));   // 08:00 Vietnam
        many.Add((day.AddHours(5), 5000, 50));                              // 12:00, single post: not trusted
        var result = GrowthInsightsService.ComputeHours(many, 1, "test");
        result.EnoughData.Should().BeTrue();
        result.SuggestedSlots.Should().Equal("20:00");
    }

    [Fact]
    public void MetaOneVerdictComparesWithTheWeekBeforeAndNeverClaimsCause()
    {
        GrowthPeriodDto P(long? follows, int days) => new() { NewFollows = follows, DaysWithData = days };
        GrowthReportService.Decide(null, null, null, 7).Verdict.Should().Be(GrowthReportService.NotEnoughData);
        var cont = GrowthReportService.Decide(P(14, 7), P(70, 7), 70, 7);
        cont.Verdict.Should().Be(GrowthReportService.Continue);
        cont.Reason.Should().Contain("không thể quy toàn bộ cho Meta One");
        GrowthReportService.Decide(P(70, 7), P(80, 7), 80, 7).Verdict.Should().Be(GrowthReportService.TestMore);
        GrowthReportService.Decide(P(70, 7), P(60, 7), 60, 7).Verdict.Should().Be(GrowthReportService.DontBuy);
        GrowthReportService.Decide(null, null, 35, 7).Verdict.Should().Be(GrowthReportService.TestMore); // no baseline
        GrowthReportService.Decide(null, null, -3, 7).Verdict.Should().Be(GrowthReportService.DontBuy);
    }

    [Fact]
    public void AiAnswersAreParsedDefensively()
    {
        GrowthAiService.ParseVariants("not json").Should().BeEmpty();
        var v = GrowthAiService.ParseVariants("{\"variants\":[{\"style\":\"Emotional\",\"caption\":\"Một buổi chiều\",\"hashtags\":[\"tuoitho\",\"#a\",\"#b\",\"#c\",\"#d\",\"#e\"]},{\"caption\":\"\"}]}");
        v.Should().ContainSingle();
        v[0].Hashtags.Should().HaveCount(5).And.Contain("#tuoitho");
        var r = GrowthAiService.ParseRepurpose("{\"photoPosts\":[{\"headline\":\"A\",\"body\":\"B\",\"caption\":\"C\"}],\"stories\":[{\"text\":\"S1\"}],\"captions\":[{\"style\":\"Short\",\"caption\":\"Cap\"}],\"titles\":[\"T1\",\"T1\",\"T2\"],\"hashtags\":[\"#x\"],\"commentQuestion\":\"Q?\",\"followCta\":\"Theo dõi nhé\"}");
        r.PhotoPosts.Should().ContainSingle();
        r.Stories.Should().ContainSingle();
        r.Captions.Should().ContainSingle();
        r.Titles.Should().Equal("T1", "T2");
        r.FollowCta.Should().Be("Theo dõi nhé");
        GrowthAiService.ParseRepurpose("[]").PhotoPosts.Should().BeEmpty();
    }

    [Fact]
    public void CampaignAiBudgetIsEnforced()
    {
        var c = new GrowthCampaign { AiBudgetUsd = 0.05m, AiSpentUsd = 0.045m };
        var over = () => GrowthAiService.EnsureCampaignBudget(c, 0.01m);
        over.Should().Throw<InvalidOperationException>().WithMessage("*ngân sách AI*");
        var zero = () => GrowthAiService.EnsureCampaignBudget(new GrowthCampaign { AiBudgetUsd = 0 }, 0.01m);
        zero.Should().Throw<InvalidOperationException>();
        GrowthAiService.EnsureCampaignBudget(new GrowthCampaign { AiBudgetUsd = 1 }, 0.01m);
    }

    [Fact]
    public void UploadsAreRecognisedByTheirBytesNotTheirName()
    {
        GrowthContentService.Sniff(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }).Should().Be(("Image", "image/jpeg"));
        GrowthContentService.Sniff(Png(3, 2)).Should().Be(("Image", "image/png"));
        GrowthContentService.Sniff(new byte[] { 0, 0, 0, 0x18 }.Concat(Encoding.ASCII.GetBytes("ftypmp42")).ToArray()).Should().Be(("Video", "video/mp4"));
        GrowthContentService.Sniff(Encoding.ASCII.GetBytes("<html><script>")).Should().BeNull();
        GrowthContentService.Sniff(Encoding.ASCII.GetBytes("GIF89a")).Should().BeNull();
        GrowthContentService.ImageSize(Png(1080, 1350)).Should().Be((1080, 1350));
        GrowthContentService.ImageSize(Jpeg(640, 480)).Should().Be((640, 480));
    }

    [Fact]
    public void OnlyFacebookLinksAreAccepted()
    {
        GrowthContentService.IsFacebookUrl("https://www.facebook.com/somepage").Should().BeTrue();
        GrowthContentService.IsFacebookUrl("https://fb.watch/abc").Should().BeTrue();
        GrowthContentService.IsFacebookUrl("http://facebook.com/x").Should().BeFalse();
        GrowthContentService.IsFacebookUrl("https://facebook.com.evil.example/x").Should().BeFalse();
        GrowthContentService.IsFacebookUrl("javascript:alert(1)").Should().BeFalse();
        GrowthCompetitorService.ValidateUrl("facebook.com/rival").Should().Be("https://facebook.com/rival");
    }

    [Fact]
    public void InsightDayValuesAreFiledUnderTheDayTheyCover()
    {
        var json = JsonDocument.Parse("""
            {"data":[{"name":"page_daily_follows_unique","period":"day","values":[
              {"value":12,"end_time":"2026-11-25T08:00:00+0000"},
              {"value":7,"end_time":"2026-11-26T08:00:00+0000"},
              {"value":3,"end_time":"2026-12-30T08:00:00+0000"}]}]}
            """).RootElement;
        var values = GrowthGraphClient.ParseDayValues(json, "page_daily_follows_unique", new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 30));
        values.Should().HaveCount(2);
        values[new DateOnly(2026, 11, 24)].Should().Be(12);
        values[new DateOnly(2026, 11, 25)].Should().Be(7);
    }

    [Fact]
    public void ReportExportsEscapeFormulasAndMarkup()
    {
        var report = new GrowthReportDto
        {
            Verdict = GrowthReportService.NotEnoughData, VerdictLabel = "x", VerdictReason = "y",
            Data = new GrowthReportDataDto { CampaignName = "=HYPERLINK(\"http://evil\")", PageName = "<b>Page</b>", StartDate = new DateOnly(2026, 12, 1), EndDate = new DateOnly(2026, 12, 7) }
        };
        var posts = new List<GrowthPostAnalyticsDto> { new() { Kind = "Photo", Title = "+cmd", Views = 10 } };
        var csv = GrowthReportService.Csv(report, posts);
        csv.Should().Contain("\"'=HYPERLINK(\"\"http://evil\"\")\"");
        csv.Should().Contain("\"'+cmd\"");
        csv.Should().Contain("Chưa có dữ liệu"); // missing numbers are said, never 0
        var xml = GrowthReportService.SpreadsheetXml(report, posts);
        xml.Should().Contain("&lt;b&gt;Page&lt;/b&gt;").And.NotContain("<b>Page</b>");
    }

    internal static byte[] Png(int w, int h)
    {
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        b[16] = (byte)(w >> 24); b[17] = (byte)(w >> 16); b[18] = (byte)(w >> 8); b[19] = (byte)w;
        b[20] = (byte)(h >> 24); b[21] = (byte)(h >> 16); b[22] = (byte)(h >> 8); b[23] = (byte)h;
        return b;
    }

    internal static byte[] Jpeg(int w, int h) => new byte[]
    {
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, // APP0 (length 4)
        0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(h >> 8), (byte)h, (byte)(w >> 8), (byte)w, 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xD9
    };
}

// ═══════════════════════════════ API / integration ═══════════════════════════════

[Collection("DatabaseCollection")]
public class GrowthApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Key = "test-growth-studio-key";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public GrowthApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>Runs with a studio key configured on the shared host (like the studio guard tests).</summary>
    private async Task WithKey(Func<Task> test)
    {
        var config = _factory.Services.GetRequiredService<IConfiguration>();
        var before = config["Ai:Gemini:ApiKey"];
        config["Ai:Gemini:ApiKey"] = Key;
        try { await test(); }
        finally { config["Ai:Gemini:ApiKey"] = before; }
    }

    private Task<HttpResponseMessage> Send(string method, string url, object? body = null, string? key = Key)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), url);
        if (body != null) req.Content = JsonContent.Create(body);
        if (key != null) req.Headers.Add("X-Studio-Key", key);
        return _client.SendAsync(req);
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static async Task<T> Read<T>(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        res.IsSuccessStatusCode.Should().BeTrue($"{(int)res.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, Web)!;
    }

    private async Task<FacebookAccount> NewPageAsync(bool active = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var page = new FacebookAccount { PageId = "9" + Random.Shared.NextInt64(100000000, 999999999), PageName = "Growth Test " + Guid.NewGuid().ToString("N")[..6], EncryptedAccessToken = active ? "test-token" : "", IsActive = active };
        db.FacebookAccounts.Add(page);
        await db.SaveChangesAsync();
        return page;
    }

    private async Task<GrowthCampaignDto> NewCampaignAsync(Guid pageId, string mode = "Auto") =>
        await Read<GrowthCampaignDto>(await Send("POST", "/api/growth/campaigns", new { facebookAccountId = pageId, approvalMode = mode, aiBudgetUsd = 0.5 }));

    private async Task<GrowthAssetDto> UploadPngAsync(Guid campaignId)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(GrowthLogicTests.Png(1080, 1350));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "anh.png");
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/growth/campaigns/{campaignId}/assets") { Content = form };
        req.Headers.Add("X-Studio-Key", Key);
        return await Read<GrowthAssetDto>(await _client.SendAsync(req));
    }

    private async Task SetStatusAsync(Guid campaignId, string status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var c = await db.GrowthCampaigns.FirstAsync(x => x.Id == campaignId);
        c.Status = status;
        await db.SaveChangesAsync();
    }

    [Fact]
    public Task EveryGrowthEndpointNeedsTheStudioKey() => WithKey(async () =>
    {
        foreach (var (method, url) in new[] { ("GET", "/api/growth/pages"), ("GET", "/api/growth/campaigns"), ("POST", "/api/growth/campaigns"),
                     ("GET", $"/api/growth/campaigns/{Guid.NewGuid()}/overview"), ("GET", $"/api/growth/competitors?pageId={Guid.NewGuid()}"),
                     ("GET", $"/api/growth/campaigns/{Guid.NewGuid()}/report.csv") })
        {
            (await Send(method, url, method == "POST" ? new { } : null, key: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);
            (await Send(method, url, method == "POST" ? new { } : null, key: "guess")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);
        }
    });

    [Fact]
    public Task PagesComeFromTheExistingConnectionWithoutTokens() => WithKey(async () =>
    {
        var page = await NewPageAsync();
        var res = await Send("GET", "/api/growth/pages");
        var text = await res.Content.ReadAsStringAsync();
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        text.Should().Contain(page.PageName).And.NotContain("test-token");
        text.Should().NotContainEquivalentOf("accessToken");
    });

    [Fact]
    public Task DefaultCampaignIsTheSevenDayChallenge() => WithKey(async () =>
    {
        var page = await NewPageAsync();
        var c = await Read<GrowthCampaignDto>(await Send("POST", "/api/growth/campaigns", new { facebookAccountId = page.Id }));
        c.Name.Should().Be("Meta One 7-Day Growth Challenge");
        c.TotalDays.Should().Be(7);
        c.ReelsPerDay.Should().Be(1);
        c.PhotosPerDay.Should().Be(2);
        c.StoriesPerDay.Should().Be(3);
        c.Status.Should().Be("Draft");
        c.ApprovalMode.Should().Be("Review");
        c.Plan.Should().HaveCount(7);
        c.Checklist.Should().HaveCount(5).And.OnlyContain(i => !i.Done);
        c.PageName.Should().Be(page.PageName);

        (await Send("POST", "/api/growth/campaigns", new { facebookAccountId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send("POST", "/api/growth/campaigns", new { facebookAccountId = page.Id, days = 7, timeSlots = new[] { "26:00" } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send("POST", "/api/growth/campaigns", new { facebookAccountId = page.Id, startDate = "2020-01-01" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    });

    [Fact]
    public Task LifecycleStartPauseResumeCompleteAndOnlyOneRunningPerPage() => WithKey(async () =>
    {
        var page = await NewPageAsync();
        var c = await NewCampaignAsync(page.Id);
        var other = await NewCampaignAsync(page.Id);
        (await Send("POST", $"/api/growth/campaigns/{c.Id}/pause")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var started = await Read<GrowthCampaignDto>(await Send("POST", $"/api/growth/campaigns/{c.Id}/start"));
        started.Status.Should().Be("Running");
        started.StartedAtUtc.Should().NotBeNull();
        (await Send("POST", $"/api/growth/campaigns/{other.Id}/start")).StatusCode.Should().Be(HttpStatusCode.Conflict); // one at a time per Page
        (await Read<GrowthCampaignDto>(await Send("POST", $"/api/growth/campaigns/{c.Id}/pause"))).Status.Should().Be("Paused");
        (await Read<GrowthCampaignDto>(await Send("POST", $"/api/growth/campaigns/{c.Id}/resume"))).Status.Should().Be("Running");
        (await Read<GrowthCampaignDto>(await Send("POST", $"/api/growth/campaigns/{c.Id}/complete"))).Status.Should().Be("Completed");
        (await Send("PUT", $"/api/growth/campaigns/{c.Id}", new { name = "x" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var logs = await Read<List<GrowthActivityLogDto>>(await Send("GET", $"/api/growth/campaigns/{c.Id}/logs"));
        logs.Select(l => l.Action).Should().Contain(new[] { "CampaignCreated", "CampaignStarted", "CampaignPaused", "CampaignResumed", "CampaignCompleted" });
    });

    [Fact]
    public Task DisconnectedPageCannotStart() => WithKey(async () =>
    {
        var page = await NewPageAsync(active: false);
        var c = await NewCampaignAsync(page.Id);
        var res = await Send("POST", $"/api/growth/campaigns/{c.Id}/start");
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await res.Content.ReadAsStringAsync()).Should().Contain("kết nối");
    });

    [Fact]
    public Task ContentFlowWithApprovalAndIdempotency() => WithKey(async () =>
    {
        var page = await NewPageAsync();
        var c = await NewCampaignAsync(page.Id, mode: "Review");
        var asset = await UploadPngAsync(c.Id);
        asset.Width.Should().Be(1080);
        var at = DateTime.UtcNow.AddHours(2);
        var key = "idem-" + Guid.NewGuid().ToString("N");
        var body = new { kind = "Photo", caption = "Ảnh đầu tiên", hashtags = "#a #b", assetIds = new[] { asset.Id }, scheduledAtUtc = at, idempotencyKey = key, submit = true };
        var first = await Read<GrowthContentDto>(await Send("POST", $"/api/growth/campaigns/{c.Id}/contents", body));
        var again = await Read<GrowthContentDto>(await Send("POST", $"/api/growth/campaigns/{c.Id}/contents", body));
        again.Id.Should().Be(first.Id); // same request twice → one item
        first.Status.Should().Be("PendingApproval");
        first.Assets.Should().ContainSingle(a => a.Id == asset.Id);

        var approved = await Read<GrowthContentDto>(await Send("POST", $"/api/growth/campaigns/{c.Id}/contents/{first.Id}/approve"));
        approved.Status.Should().Be("Scheduled");
        // changing the text of an approved item asks for approval again; moving it only does not
        var moved = await Read<GrowthContentDto>(await Send("PUT", $"/api/growth/campaigns/{c.Id}/contents/{first.Id}", new { scheduledAtUtc = at.AddHours(1) }));
        moved.Status.Should().Be("Scheduled");
        var edited = await Read<GrowthContentDto>(await Send("PUT", $"/api/growth/campaigns/{c.Id}/contents/{first.Id}", new { caption = "Đổi chữ" }));
        edited.Status.Should().Be("PendingApproval");

        var list = await Read<List<GrowthContentDto>>(await Send("GET", $"/api/growth/campaigns/{c.Id}/contents"));
        list.Should().ContainSingle(x => x.Id == first.Id);
        (await Read<GrowthContentDto>(await Send("POST", $"/api/growth/campaigns/{c.Id}/contents/{first.Id}/cancel"))).Status.Should().Be("Cancelled");
        (await Send("DELETE", $"/api/growth/campaigns/{c.Id}/contents/{first.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    });

    [Fact]
    public Task ContentRulesAreValidated() => WithKey(async () =>
    {
        var page = await NewPageAsync();
        var c = await NewCampaignAsync(page.Id);
        var url = $"/api/growth/campaigns/{c.Id}/contents";
        (await Send("POST", url, new { kind = "Banner" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send("POST", url, new { kind = "Interactive", caption = string.Join(" ", Enumerable.Range(1, 11).Select(i => "#tag" + i)) })).StatusCode.Should().Be(HttpStatusCode.BadRequest); // hashtag spam
        (await Send("POST", url, new { kind = "Photo", caption = "x", submit = true, scheduledAtUtc = DateTime.UtcNow.AddHours(1) })).StatusCode.Should().Be(HttpStatusCode.BadRequest); // no picture
        (await Send("POST", url, new { kind = "Interactive", caption = "Câu hỏi?", submit = true, scheduledAtUtc = DateTime.UtcNow.AddDays(30) })).StatusCode.Should().Be(HttpStatusCode.BadRequest); // outside campaign
        (await Send("POST", url, new { kind = "Interactive", caption = "Câu hỏi?", submit = true, scheduledAtUtc = DateTime.UtcNow.AddHours(-3) })).StatusCode.Should().Be(HttpStatusCode.BadRequest); // in the past
        (await Send("POST", url, new { kind = "Reel", sourceType = "StudioVideo", sourceId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.BadRequest); // unknown video
        var ok = await Read<GrowthContentDto>(await Send("POST", url, new { kind = "Interactive", caption = "Bạn nhớ nhất món gì hồi nhỏ?", submit = true, scheduledAtUtc = DateTime.UtcNow.AddHours(1) }));
        ok.Status.Should().Be("Scheduled"); // Auto mode
    });

    [Fact]
    public Task UploadsRejectWrongTypes() => WithKey(async () =>
    {
        var page = await NewPageAsync();
        var c = await NewCampaignAsync(page.Id);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("<svg onload=alert(1)>"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png"); // lies about its type
        form.Add(file, "file", "evil.png");
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/growth/campaigns/{c.Id}/assets") { Content = form };
        req.Headers.Add("X-Studio-Key", Key);
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    });

    [Fact]
    public Task ItemsOfAnotherCampaignAreNotReachable_Idor() => WithKey(async () =>
    {
        var pageA = await NewPageAsync();
        var pageB = await NewPageAsync();
        var a = await NewCampaignAsync(pageA.Id);
        var b = await NewCampaignAsync(pageB.Id);
        var assetB = await UploadPngAsync(b.Id);
        var itemB = await Read<GrowthContentDto>(await Send("POST", $"/api/growth/campaigns/{b.Id}/contents", new { kind = "Interactive", caption = "Của B" }));

        // B's item through A's URL
        (await Send("GET", $"/api/growth/campaigns/{a.Id}/contents/{itemB.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Send("PUT", $"/api/growth/campaigns/{a.Id}/contents/{itemB.Id}", new { caption = "hack" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Send("POST", $"/api/growth/campaigns/{a.Id}/contents/{itemB.Id}/publish-now")).StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await Send("DELETE", $"/api/growth/campaigns/{a.Id}/assets/{assetB.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // B's picture attached to A's content
        (await Send("POST", $"/api/growth/campaigns/{a.Id}/contents", new { kind = "Photo", assetIds = new[] { assetB.Id } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // the same idempotency key can't reach B's item from A
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var keyB = (await db.GrowthScheduledContents.AsNoTracking().FirstAsync(x => x.Id == itemB.Id)).IdempotencyKey;
            (await Send("POST", $"/api/growth/campaigns/{a.Id}/contents", new { kind = "Interactive", caption = "x", idempotencyKey = keyB })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        // a competitor of page B through page A
        var rival = await Read<GrowthCompetitorDto>(await Send("POST", "/api/growth/competitors", new { facebookAccountId = pageB.Id, name = "Đối thủ", url = "https://facebook.com/rival-" + Guid.NewGuid().ToString("N")[..6] }));
        (await Send("DELETE", $"/api/growth/competitors/{rival.Id}?pageId={pageA.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Send("POST", $"/api/growth/competitors/{rival.Id}/snapshots?pageId={pageA.Id}", new { followers = 5 })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    });

    [Fact]
    public Task AtMostFiveCompetitorsPerPage() => WithKey(async () =>
    {
        var page = await NewPageAsync();
        for (var i = 0; i < 5; i++)
            (await Send("POST", "/api/growth/competitors", new { facebookAccountId = page.Id, name = "R" + i, url = $"https://www.facebook.com/r{i}-{Guid.NewGuid():N}" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send("POST", "/api/growth/competitors", new { facebookAccountId = page.Id, name = "R6", url = "https://www.facebook.com/r6" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Send("POST", "/api/growth/competitors", new { facebookAccountId = page.Id, name = "Bad", url = "https://evil.example/x" })).StatusCode.Should().NotBe(HttpStatusCode.OK);
        var list = await Read<List<GrowthCompetitorDto>>(await Send("GET", $"/api/growth/competitors?pageId={page.Id}"));
        list.Should().HaveCount(5);
        var snap = await Read<GrowthCompetitorDto>(await Send("POST", $"/api/growth/competitors/{list[0].Id}/snapshots?pageId={page.Id}",
            new { followers = 12000, postsPerWeek = 10, format = "Reel", topic = "Tuổi thơ", postTitle = "Bài hay", reactions = 300 }));
        snap.LatestFollowers.Should().Be(12000);
        var analysis = await Read<GrowthCompetitorAnalysisDto>(await Send("POST", $"/api/growth/competitors/analyze?pageId={page.Id}"));
        analysis.Observations.Should().Be(1);
        analysis.Formats.Should().ContainSingle(f => f.Label == "Reel");
    });

    [Fact]
    public Task OverviewShowsNoDataInsteadOfZeros() => WithKey(async () =>
    {
        var page = await NewPageAsync();
        var c = await NewCampaignAsync(page.Id);
        var o = await Read<GrowthOverviewDto>(await Send("GET", $"/api/growth/campaigns/{c.Id}/overview"));
        o.Kpis.Should().Contain(k => k.Key == "views" && k.Value == null && k.Note != null);
        o.Kpis.Should().Contain(k => k.Key == "non_follower_views" && k.Value == null && k.Note!.Contains("Meta"));
        o.Kpis.Should().Contain(k => k.Key == "growth_rate" && k.Value == null);
        o.Kpis.Should().Contain(k => k.Key == "posts" && k.Value == 0);
        o.Daily.Should().NotBeEmpty();
        o.Daily.Should().OnlyContain(d => d.NewFollows == null && d.PageViews == null);
    });

    [Fact]
    public Task KpisAndReportUseStoredMetaNumbers() => WithKey(async () =>
    {
        var page = await NewPageAsync();
        var c = await NewCampaignAsync(page.Id);
        var today = GrowthTime.Today(DateTime.UtcNow);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var campaign = await db.GrowthCampaigns.FirstAsync(x => x.Id == c.Id);
            campaign.Status = "Running";
            campaign.StartingFollowers = 1000;
            campaign.StartedAtUtc = DateTime.UtcNow;
            db.GrowthDailyMetrics.Add(new GrowthDailyMetric { FacebookAccountId = page.Id, Date = today, Followers = 1050, NewFollows = 55, PageViews = 500, CollectedAtUtc = DateTime.UtcNow });
            for (var i = 1; i <= 7; i++)
                db.GrowthDailyMetrics.Add(new GrowthDailyMetric { FacebookAccountId = page.Id, Date = today.AddDays(-i), NewFollows = 2 });
            var post = new GrowthScheduledContent { CampaignId = c.Id, Kind = "Photo", Status = "Published", Caption = "Bài 1", IdempotencyKey = Guid.NewGuid().ToString("N"), PublishedAtUtc = DateTime.UtcNow.AddHours(-2), ExternalPostId = "1_2" };
            db.GrowthScheduledContents.Add(post);
            await db.SaveChangesAsync();
            db.GrowthPostMetrics.Add(new GrowthPostMetric { ContentId = post.Id, Date = today, Views = 800, Reach = 600, Reactions = 40, Comments = 5, Shares = 7, CollectedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var o = await Read<GrowthOverviewDto>(await Send("GET", $"/api/growth/campaigns/{c.Id}/overview"));
        o.Kpis.Single(k => k.Key == "new_followers").Value.Should().Be(50);
        o.Kpis.Single(k => k.Key == "growth_rate").Value.Should().Be(5);
        o.Kpis.Single(k => k.Key == "page_visits").Value.Should().Be(500);
        o.Kpis.Single(k => k.Key == "conversion").Value.Should().Be(10);
        o.Kpis.Single(k => k.Key == "views").Value.Should().Be(800);
        o.Kpis.Single(k => k.Key == "engagement").Value.Should().Be(52);
        o.Before!.NewFollows.Should().Be(14);

        var report = await Read<GrowthReportDto>(await Send("POST", $"/api/growth/campaigns/{c.Id}/report"));
        report.Data.FollowerGain.Should().Be(50);
        report.Verdict.Should().Be(GrowthReportService.Continue); // 55/day vs 2/day before
        report.VerdictReason.Should().Contain("không thể quy toàn bộ");
        report.Data.DataGaps.Should().Contain(g => g.Contains("chi phí Meta One"));
        var csv = await Send("GET", $"/api/growth/campaigns/{c.Id}/report.csv");
        csv.StatusCode.Should().Be(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        (await csv.Content.ReadAsStringAsync()).Should().Contain("Bài 1");
        (await Send("GET", $"/api/growth/campaigns/{c.Id}/report.xls")).StatusCode.Should().Be(HttpStatusCode.OK);
    });

    [Fact]
    public Task ManualMetricsAndChecklistAreRecorded() => WithKey(async () =>
    {
        var page = await NewPageAsync();
        var c = await NewCampaignAsync(page.Id);
        var today = GrowthTime.Today(DateTime.UtcNow);
        (await Send("PUT", $"/api/growth/campaigns/{c.Id}/manual-metrics", new { date = today.ToString("yyyy-MM-dd"), invitesSent = 10, invitesAccepted = 20 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send("PUT", $"/api/growth/campaigns/{c.Id}/manual-metrics", new { date = today.AddDays(1).ToString("yyyy-MM-dd"), invitesSent = 1 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send("PUT", $"/api/growth/campaigns/{c.Id}/manual-metrics", new { date = today.ToString("yyyy-MM-dd"), invitesSent = 40, invitesAccepted = 12 })).StatusCode.Should().Be(HttpStatusCode.OK);
        var f = await Read<GrowthFollowersDto>(await Send("GET", $"/api/growth/campaigns/{c.Id}/analytics/followers"));
        f.InvitesSent.Should().Be(40);
        f.InvitesAccepted.Should().Be(12);
        var updated = await Read<GrowthCampaignDto>(await Send("PUT", $"/api/growth/campaigns/{c.Id}/checklist", new { key = "follow_button", done = true }));
        updated.Checklist.Single(i => i.Key == "follow_button").Done.Should().BeTrue();
        (await Send("PUT", $"/api/growth/campaigns/{c.Id}/checklist", new { key = "unknown", done = true })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    });

    // ───────────── scheduler ─────────────

    private sealed class FakeGateway : IGrowthPublishGateway
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<Guid> Ids = new();
        public int CallsFor(Guid id) => Ids.Count(x => x == id);
        public Func<GrowthScheduledContent, Exception?> Fail = _ => null;
        public string? InstagramError;
        public async Task<GrowthPublishResult> PublishAsync(GrowthCampaign campaign, GrowthScheduledContent item, CancellationToken ct)
        {
            Ids.Enqueue(item.Id);
            await Task.Delay(30, ct);
            if (Fail(item) is { } ex) throw ex;
            return InstagramError != null
                ? new GrowthPublishResult("123_" + item.Id.ToString("N")[..8], null, "https://www.facebook.com/123", null) { InstagramError = InstagramError }
                : new GrowthPublishResult("123_" + item.Id.ToString("N")[..8], null, "https://www.facebook.com/123", null) { InstagramMediaId = "1789", InstagramPermalink = "https://www.instagram.com/p/abc/" };
        }
    }

    private GrowthPublisher Publisher(IServiceScope scope, FakeGateway gateway) => new(
        scope.ServiceProvider.GetRequiredService<IApplicationDbContext>(), gateway, scope.ServiceProvider.GetRequiredService<GrowthCampaignService>(),
        scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>(), NullLogger<GrowthPublisher>.Instance);

    private async Task<Guid> DueItemAsync(Guid campaignId, string campaignStatus = "Running", DateTime? at = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var c = await db.GrowthCampaigns.FirstAsync(x => x.Id == campaignId);
        c.Status = campaignStatus;
        var item = new GrowthScheduledContent
        {
            CampaignId = campaignId, Kind = "Interactive", Status = "Scheduled", Caption = "Câu hỏi hôm nay?",
            ScheduledAtUtc = at ?? DateTime.UtcNow.AddMinutes(-1), IdempotencyKey = Guid.NewGuid().ToString("N")
        };
        db.GrowthScheduledContents.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private async Task<GrowthScheduledContent> ItemAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IApplicationDbContext>().GrowthScheduledContents.AsNoTracking().FirstAsync(x => x.Id == id);
    }

    [Fact]
    public async Task ConcurrentWorkersPublishAnItemOnlyOnce()
    {
        var page = await NewPageAsync();
        var c = await WithKeyResult(() => NewCampaignAsync(page.Id));
        var id = await DueItemAsync(c.Id);
        var gateway = new FakeGateway();
        // PC and Railway pick the same item at the same moment
        var runs = Enumerable.Range(0, 4).Select(async _ =>
        {
            using var scope = _factory.Services.CreateScope();
            await Publisher(scope, gateway).PublishOneAsync(id, CancellationToken.None);
        });
        await Task.WhenAll(runs);
        gateway.CallsFor(id).Should().Be(1);
        var item = await ItemAsync(id);
        item.Status.Should().Be("Published");
        item.ExternalPostId.Should().StartWith("123_");
        // asking again never posts it a second time
        using (var scope = _factory.Services.CreateScope()) await Publisher(scope, gateway).PublishOneAsync(id, CancellationToken.None);
        gateway.CallsFor(id).Should().Be(1);
    }

    [Fact]
    public async Task TheInstagramCopyIsRecordedAndItsFailureNeverUndoesFacebook()
    {
        var page = await NewPageAsync();
        var c = await WithKeyResult(() => NewCampaignAsync(page.Id));
        c.AlsoInstagram.Should().BeTrue("posting on Facebook also posts on Instagram by default");
        var ok = await DueItemAsync(c.Id);
        var failing = await DueItemAsync(c.Id);
        using (var scope = _factory.Services.CreateScope()) await Publisher(scope, new FakeGateway()).PublishOneAsync(ok, CancellationToken.None);
        using (var scope = _factory.Services.CreateScope())
            await Publisher(scope, new FakeGateway { InstagramError = "Page chưa liên kết tài khoản Instagram" }).PublishOneAsync(failing, CancellationToken.None);
        var a = await ItemAsync(ok);
        a.InstagramMediaId.Should().Be("1789");
        a.InstagramPermalink.Should().Contain("instagram.com");
        var b = await ItemAsync(failing);
        b.Status.Should().Be("Published", "Facebook went out; Instagram failing is only noted");
        b.InstagramError.Should().Contain("Instagram");
    }

    [Fact]
    public async Task NothingIsPostedWhilePausedOrAfterTheEnd()
    {
        var page = await NewPageAsync();
        var c = await WithKeyResult(() => NewCampaignAsync(page.Id));
        var paused = await DueItemAsync(c.Id, "Paused");
        var gateway = new FakeGateway();
        using (var scope = _factory.Services.CreateScope()) await Publisher(scope, gateway).PublishOneAsync(paused, CancellationToken.None);
        gateway.CallsFor(paused).Should().Be(0);
        (await ItemAsync(paused)).Status.Should().Be("Scheduled");

        // a campaign past its last day is completed by the tick and its waiting items are cancelled
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var camp = await db.GrowthCampaigns.FirstAsync(x => x.Id == c.Id);
            camp.Status = "Running";
            camp.StartDate = GrowthTime.Today(DateTime.UtcNow).AddDays(-8);
            camp.EndDate = GrowthTime.Today(DateTime.UtcNow).AddDays(-2);
            await db.SaveChangesAsync();
        }
        using (var scope = _factory.Services.CreateScope()) await Publisher(scope, gateway).TickAsync(CancellationToken.None);
        gateway.CallsFor(paused).Should().Be(0);
        (await ItemAsync(paused)).Status.Should().Be("Cancelled");
        using (var scope = _factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<IApplicationDbContext>().GrowthCampaigns.AsNoTracking().FirstAsync(x => x.Id == c.Id)).Status.Should().Be("Completed");
    }

    [Fact]
    public async Task TemporaryErrorsAreRetriedLaterButTokenErrorsAreNot()
    {
        var page = await NewPageAsync();
        var c = await WithKeyResult(() => NewCampaignAsync(page.Id));
        var transient = await DueItemAsync(c.Id);
        var token = await DueItemAsync(c.Id);
        var gateway = new FakeGateway
        {
            Fail = item => item.Id == transient ? new HttpRequestException("connection reset")
                : new InvalidOperationException("Meta báo lỗi: Error validating access token: Session has expired")
        };
        using (var scope = _factory.Services.CreateScope()) await Publisher(scope, gateway).PublishOneAsync(transient, CancellationToken.None);
        using (var scope = _factory.Services.CreateScope()) await Publisher(scope, gateway).PublishOneAsync(token, CancellationToken.None);

        var t = await ItemAsync(transient);
        t.Status.Should().Be("Scheduled");
        t.Attempts.Should().Be(1);
        t.NextAttemptAtUtc.Should().BeAfter(DateTime.UtcNow.AddSeconds(60));
        t.ErrorKind.Should().Be(GrowthErrorKind.Transient);

        var k = await ItemAsync(token);
        k.Status.Should().Be("Failed");
        k.ErrorKind.Should().Be(GrowthErrorKind.Token);
        k.LastError.Should().Contain("kết nối lại");

        // the scheduler waits for the retry time instead of hammering Meta
        using (var scope = _factory.Services.CreateScope()) await Publisher(scope, gateway).TickAsync(CancellationToken.None);
        (await ItemAsync(transient)).Attempts.Should().Be(1);

        // after the last allowed attempt a temporary error also ends as Failed
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var row = await db.GrowthScheduledContents.FirstAsync(x => x.Id == transient);
            row.Attempts = row.MaxAttempts - 1;
            row.NextAttemptAtUtc = null;
            await db.SaveChangesAsync();
        }
        using (var scope = _factory.Services.CreateScope()) await Publisher(scope, gateway).PublishOneAsync(transient, CancellationToken.None);
        (await ItemAsync(transient)).Status.Should().Be("Failed");
        gateway.CallsFor(transient).Should().Be(2);
    }

    [Fact]
    public async Task ItemsStuckWhilePostingAreFlaggedNotRepublished()
    {
        var page = await NewPageAsync();
        var c = await WithKeyResult(() => NewCampaignAsync(page.Id));
        var id = await DueItemAsync(c.Id);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var row = await db.GrowthScheduledContents.FirstAsync(x => x.Id == id);
            row.Status = "Publishing";
            row.PublishStartedAtUtc = DateTime.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
        }
        var gateway = new FakeGateway();
        using (var scope = _factory.Services.CreateScope()) await Publisher(scope, gateway).TickAsync(CancellationToken.None);
        gateway.CallsFor(id).Should().Be(0);
        var item = await ItemAsync(id);
        item.Status.Should().Be("Failed");
        item.LastError.Should().Contain("kiểm tra");
    }

    [Fact]
    public Task CancelledOrPublishedItemsCannotBePostedAgain() => WithKey(async () =>
    {
        var page = await NewPageAsync();
        var c = await NewCampaignAsync(page.Id);
        await SetStatusAsync(c.Id, "Running");
        var id = await DueItemAsync(c.Id);
        var gateway = new FakeGateway();
        using (var scope = _factory.Services.CreateScope()) await Publisher(scope, gateway).PublishOneAsync(id, CancellationToken.None);
        (await Send("POST", $"/api/growth/campaigns/{c.Id}/contents/{id}/publish-now")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Send("POST", $"/api/growth/campaigns/{c.Id}/contents/{id}/retry")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Send("POST", $"/api/growth/campaigns/{c.Id}/contents/{id}/cancel")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        gateway.CallsFor(id).Should().Be(1);
    });

    [Fact]
    public Task ManualStoryIsRecordedAsPostedByHand() => WithKey(async () =>
    {
        var page = await NewPageAsync();
        var c = await NewCampaignAsync(page.Id);
        var asset = await UploadPngAsync(c.Id);
        var story = await Read<GrowthContentDto>(await Send("POST", $"/api/growth/campaigns/{c.Id}/contents",
            new { kind = "Story", assetIds = new[] { asset.Id }, manualPublish = true, scheduledAtUtc = DateTime.UtcNow.AddHours(1), submit = true }));
        story.Status.Should().Be("Scheduled");
        (await Send("POST", $"/api/growth/campaigns/{c.Id}/contents/{story.Id}/mark-manual", new { permalinkUrl = "https://evil.example" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var done = await Read<GrowthContentDto>(await Send("POST", $"/api/growth/campaigns/{c.Id}/contents/{story.Id}/mark-manual", new { permalinkUrl = "https://www.facebook.com/stories/1" }));
        done.Status.Should().Be("Published");
        done.PublishedManually.Should().BeTrue();
    });

    [Fact]
    public Task AiStopsAtTheCampaignBudgetAndSuggestionsWorkWithoutAi() => WithKey(async () =>
    {
        var page = await NewPageAsync();
        var c = await Read<GrowthCampaignDto>(await Send("POST", "/api/growth/campaigns", new { facebookAccountId = page.Id, aiBudgetUsd = 0 }));
        var res = await Send("POST", $"/api/growth/campaigns/{c.Id}/ai/captions", new { brief = "Một video về tuổi thơ thả diều ở quê", styles = new[] { "Emotional" } });
        res.StatusCode.Should().Be(HttpStatusCode.Conflict); // refused before any paid call
        (await res.Content.ReadAsStringAsync()).Should().Contain("Ngân sách AI");
        (await Send("POST", $"/api/growth/campaigns/{c.Id}/ai/captions", new { brief = "ngắn" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // rule-based suggestions need no AI and work with no data
        var s = await Read<GrowthSuggestionsDto>(await Send("POST", $"/api/growth/campaigns/{c.Id}/ai/suggestions"));
        s.FromAi.Should().BeFalse();
        s.Suggestions.Should().NotBeEmpty();
    });

    [Fact]
    public async Task RemovingAPageKeepsItsCampaignHistory()
    {
        var page = await NewPageAsync();
        var c = await WithKeyResult(() => NewCampaignAsync(page.Id));
        using (var scope = _factory.Services.CreateScope())
        {
            var fb = scope.ServiceProvider.GetRequiredService<NewsStudio.Application.Interfaces.IFacebookPublishingService>();
            (await fb.DeletePageAsync(page.Id)).Should().BeTrue(); // the existing delete keeps working
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var kept = await db.GrowthCampaigns.AsNoTracking().FirstAsync(x => x.Id == c.Id);
            kept.FacebookAccountId.Should().BeNull();
            kept.PageName.Should().Be(page.PageName);
        }
    }

    private async Task<T> WithKeyResult<T>(Func<Task<T>> f)
    {
        T result = default!;
        await WithKey(async () => result = await f());
        return result;
    }
}
