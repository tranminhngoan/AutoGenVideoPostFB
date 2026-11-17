using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Api;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Ai;
using NewsStudio.Infrastructure.Services;
using Xunit;

namespace NewsStudio.Tests;

public class EditorialPassRulesTests
{
    [Fact]
    public void CaptionGetsSourceAndCleanHashtagsAndTheCommentGetsTheArticleLink()
    {
        var pass = EditorialAssistantService.Parse(
            "```json\n{\"consistent\": true, \"issues\": [], \"caption\": \"SCB không trả lãi tiền gửi online\\n\\n▪️ Lãi suất 0%\", " +
            "\"hashtags\": [\"#TinNong\", \"Kinh Tế\", \"#TinNong\", \"#\"], \"first_comment\": \"Bạn sẽ rút tiền khỏi SCB chứ?\"}\n```",
            "Theo VnExpress", "https://vnexpress.net/a.html");

        pass.Consistent.Should().BeTrue();
        pass.Caption.Should().Be("SCB không trả lãi tiền gửi online\n\n▪️ Lãi suất 0%\n\nTheo VnExpress\n\n#TinNong #KinhTế");
        pass.FirstComment.Should().Be("Bạn sẽ rút tiền khỏi SCB chứ?\n\n📰 Đọc chi tiết: https://vnexpress.net/a.html");
    }

    [Fact]
    public void AMismatchWithTheArticleIsReported()
    {
        var pass = EditorialAssistantService.Parse(
            "{\"consistent\": false, \"issues\": [\"Video ghi 168 người chết, bài gốc ghi 16\"], \"caption\": \"x\", \"hashtags\": [], \"first_comment\": \"\"}",
            null, "https://e.com");
        pass.Consistent.Should().BeFalse();
        pass.Issues.Should().ContainSingle().Which.Should().Contain("168");
    }

    [Fact]
    public void WithoutTheArticleTextNothingIsBlockedOnAGuess()
    {
        var pass = EditorialAssistantService.Parse(
            "{\"consistent\": false, \"issues\": [\"không rõ\"], \"caption\": \"x\"}", null, "", hasArticle: false);
        pass.Consistent.Should().BeTrue();
        pass.FirstComment.Should().BeEmpty();
    }
}

public class InstagramRulesTests
{
    [Fact]
    public void LongCaptionsAreCutToInstagramsLimit()
    {
        NewsStudio.Infrastructure.Facebook.FacebookPublishingService.InstagramCaption("ngắn").Should().Be("ngắn");
        var cut = NewsStudio.Infrastructure.Facebook.FacebookPublishingService.InstagramCaption(new string('a', 3000));
        cut.Length.Should().Be(2200);
        cut.Should().EndWith("…");
    }

    [Fact]
    public void InstagramInsightsAreReadInBothShapes()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(
            "{\"data\": [{\"name\": \"views\", \"values\": [{\"value\": 1234}]}, " +
            "{\"name\": \"likes\", \"total_value\": {\"value\": 56}}, {\"name\": \"reach\", \"values\": []}]}");
        var values = NewsStudio.Infrastructure.Facebook.FacebookPublishingService.InstagramInsightValues(doc.RootElement);
        values["views"].Should().Be(1234);
        values["likes"].Should().Be(56);
        values.Should().NotContainKey("reach");
    }
}

public class BreakingNewsRulesTests
{
    private static readonly DateTime Noon = new(2026, 11, 16, 5, 0, 0, DateTimeKind.Utc); // 12:00 in Vietnam

    [Fact]
    public void ManyOutletsOrABigSearchWithinThreeHoursIsBreaking()
    {
        TrendingArticleDto Item(int outlets, int? searches, double hoursAgo) =>
            new() { CoverageCount = outlets, TrendTraffic = searches, PublishedAtUtc = Noon.AddHours(-hoursAgo) };

        AutoPostService.IsBreakingNews(Item(6, null, 1), Noon).Should().BeTrue();
        AutoPostService.IsBreakingNews(Item(2, 50000, 1), Noon).Should().BeTrue();
        AutoPostService.IsBreakingNews(Item(1, 50000, 1), Noon).Should().BeFalse("one outlet is not enough");
        AutoPostService.IsBreakingNews(Item(5, 5000, 1), Noon).Should().BeFalse();
        AutoPostService.IsBreakingNews(Item(9, null, 4), Noon).Should().BeFalse("too old to be breaking");
    }

    [Fact]
    public void BreakingNewsRespectsQuietHoursLimitsAndNearbySlots()
    {
        var slots = new List<string> { "07:00", "20:00" };
        AutoPostService.BreakingAllowed(Noon, slots, Array.Empty<DateTime>(), 2).Should().BeTrue();
        AutoPostService.BreakingAllowed(Noon, slots, new[] { Noon.AddHours(-3), Noon.AddHours(-5) }, 2).Should().BeFalse("daily limit");
        AutoPostService.BreakingAllowed(Noon, slots, new[] { Noon.AddHours(-1) }, 2).Should().BeFalse("2 hours between breaking posts");
        AutoPostService.BreakingAllowed(Noon, new List<string> { "12:40" }, Array.Empty<DateTime>(), 2).Should().BeFalse("a scheduled post is coming");
        AutoPostService.BreakingAllowed(Noon.AddHours(11.5), slots, Array.Empty<DateTime>(), 2).Should().BeFalse("23:30 is quiet time");
        AutoPostService.BreakingAllowed(Noon, slots, Array.Empty<DateTime>(), 0).Should().BeFalse("turned off");
    }
}

public class PageInsightsRulesTests
{
    private static PageInsightsService.Sample S(int vietnamHour, long views, string topic, string region) =>
        new(new DateTime(2026, 11, 10, vietnamHour, 5, 0, DateTimeKind.Utc).AddHours(-7), views, new[] { topic }, region);

    [Fact]
    public void FewVideosChangeNothing()
    {
        var insights = PageInsightsService.Compute(new[] { S(20, 900, "the-thao", "VN") }, 3, new[] { "07:00", "12:00", "20:00" });
        insights.EnoughData.Should().BeFalse();
        insights.SuggestedSlots.Should().BeEmpty();
        PageInsightsService.Boost(insights, new[] { "the-thao" }, "VN").Should().Be(1);
    }

    [Fact]
    public void BestHoursAndKindsOfStoriesComeOutOfTheViews()
    {
        var samples = new List<PageInsightsService.Sample>();
        for (var i = 0; i < 4; i++)
        {
            samples.Add(S(20, 1000, "thoi-su", "VN"));   // evening + current affairs: lots of views
            samples.Add(S(12, 400, "thoi-su", "VN"));
            samples.Add(S(7, 100, "the-gioi", "World")); // early international news: few views
        }
        var insights = PageInsightsService.Compute(samples, 2, new[] { "07:00", "12:30" });

        insights.EnoughData.Should().BeTrue();
        insights.AverageViews.Should().Be(500);
        insights.SuggestedSlots.Should().Equal("12:30", "20:00");
        insights.Topics.First().Key.Should().Be("thoi-su");

        var good = PageInsightsService.Boost(insights, new[] { "thoi-su" }, "VN");
        var weak = PageInsightsService.Boost(insights, new[] { "the-gioi" }, "World");
        good.Should().BeGreaterThan(1);
        weak.Should().BeLessThan(1).And.BeGreaterThanOrEqualTo(0.75 * 0.85);
    }
}

public class WeeklyDigestRulesTests
{
    [Fact]
    public void SundaysGetTheWeeklyDigestWhenEnabled()
    {
        var sunday = new DateTime(2026, 11, 22);
        DailyDigestService.KindFor(new AutoPostSettings { WeeklyDigestEnabled = true }, sunday).Should().Be(DailyDigestService.Weekly);
        DailyDigestService.KindFor(new AutoPostSettings { WeeklyDigestEnabled = false }, sunday).Should().Be(DailyDigestService.Daily);
        DailyDigestService.KindFor(new AutoPostSettings { WeeklyDigestEnabled = true }, sunday.AddDays(-1)).Should().Be(DailyDigestService.Daily);
    }

    [Fact]
    public void WeeklyCaptionNamesTheWeek()
    {
        var sunday = new DateTime(2026, 11, 22);
        DailyDigestService.DateLabel(DailyDigestService.Weekly, sunday).Should().Be("16/11 – 22/11/2026");
        var text = DailyDigestService.BuildCaption("the-thao", sunday,
            new List<DailyDigestItemDto> { new() { Headline = "A", Summary = "a", SourceName = "VnExpress" } }, DailyDigestService.Weekly);
        text.Should().StartWith("🗓 TUẦN QUA CÓ GÌ THỂ THAO (16/11 – 22/11/2026)");
        text.Should().EndWith("#TuanQuaCoGi #TinTuc");
    }
}

[Collection("DatabaseCollection")]
public class EngagementApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public EngagementApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task NewSettingsAreSavedAndInsightsWaitForEnoughVideos()
    {
        var page = new FacebookAccount { PageId = "test-engage-" + Guid.NewGuid().ToString("N")[..8], PageName = "Test Tương Tác", EncryptedAccessToken = "x" };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.FacebookAccounts.Add(page);
            await db.SaveChangesAsync();
        }

        try
        {
            var overview = await _client.GetFromJsonAsync<AutoPostOverviewDto>("/api/auto-post");
            var settings = overview!.Pages.Single(p => p.FacebookAccountId == page.Id).Settings;
            settings.BreakingEnabled.Should().BeTrue();
            settings.BreakingPerDay.Should().Be(2);
            settings.WeeklyDigestEnabled.Should().BeTrue();
            settings.AutoTune.Should().BeTrue();

            settings.InstagramEnabled.Should().BeTrue();

            settings.BreakingPerDay = 9; // capped
            settings.AutoTune = false;
            settings.WeeklyDigestEnabled = false;
            settings.InstagramEnabled = false;
            var saved = await (await _client.PutAsJsonAsync($"/api/auto-post/pages/{page.Id}/settings", settings)).Content.ReadFromJsonAsync<AutoPostSettingsDto>();
            saved!.BreakingPerDay.Should().Be(AutoPostService.MaxBreakingPerDay);
            saved.AutoTune.Should().BeFalse();
            saved.WeeklyDigestEnabled.Should().BeFalse();
            saved.InstagramEnabled.Should().BeFalse();

            // A page whose token can't be read keeps working: refreshing Instagram links never fails the list
            var refreshed = await _client.PostAsync("/api/facebook/pages/instagram/refresh", null);
            refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
            (await refreshed.Content.ReadFromJsonAsync<List<FacebookAccountDto>>())!.Should().Contain(p => p.Id == page.Id && p.InstagramUsername == null);

            var insights = await _client.GetFromJsonAsync<PageInsightsDto>($"/api/auto-post/pages/{page.Id}/insights");
            insights!.Samples.Should().Be(0);
            insights.EnoughData.Should().BeFalse();

            (await _client.PostAsync($"/api/video-projects/{Guid.NewGuid()}/editorial-pass", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.AutoPostSettings.RemoveRange(await db.AutoPostSettings.Where(s => s.FacebookAccountId == page.Id).ToListAsync());
            db.FacebookAccounts.RemoveRange(await db.FacebookAccounts.Where(a => a.Id == page.Id).ToListAsync());
            await db.SaveChangesAsync();
        }
    }
}
