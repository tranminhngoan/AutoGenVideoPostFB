using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Services;
using NewsStudio.Infrastructure.Trending;
using Xunit;

namespace NewsStudio.Tests;

public class AutoPostScheduleTests
{
    [Fact]
    public void DefaultFiveSlotsFollowVietnamPeakHours()
    {
        AutoPostService.SuggestSlots(5).Should().Equal("07:00", "11:30", "17:30", "20:00", "21:30");
    }

    [Fact]
    public void OffsetStaggersPagesSoVideosAreNotAllMadeAtOnce()
    {
        AutoPostService.SuggestSlots(5, 10).Should().Equal("07:10", "11:40", "17:40", "20:10", "21:40");
        AutoPostService.SuggestSlots(3, 50).Should().Equal("07:50", "12:50", "20:50");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    public void SuggestedSlotsMatchTheCountAndAreOrdered(int count)
    {
        var slots = AutoPostService.SuggestSlots(count);

        slots.Should().HaveCount(count);
        slots.Should().BeInAscendingOrder(StringComparer.Ordinal);
        slots.Should().OnlyHaveUniqueItems();
        AutoPostService.ParseSlots(string.Join(",", slots)).Should().Equal(slots);
    }

    [Fact]
    public void CountIsCappedAtTheDailyMaximum()
    {
        AutoPostService.SuggestSlots(50).Should().HaveCount(AutoPostService.MaxPostsPerDay);
    }

    [Fact]
    public void ParseSlotsNormalisesSortsAndDropsInvalidTimes()
    {
        AutoPostService.ParseSlots("21:30, 7:00,abc,25:00,07:00;11:30")
            .Should().Equal("07:00", "11:30", "21:30");
    }

    [Fact]
    public void MixedModeAlternatesVietnamAndInternationalNews()
    {
        Enumerable.Range(0, 5).Select(i => AutoPostService.PlannedRegion("Mixed", i))
            .Should().Equal("VN", "World", "VN", "World", "VN");
        AutoPostService.PlannedRegion("World", 0).Should().Be("World");
        AutoPostService.PlannedRegion("VN", 1).Should().Be("VN");
    }

    [Fact]
    public void SameStoryInAnotherLanguageSharesItsProperNames()
    {
        var vi = AutoPostService.ProperNames("Hơn 158.000 người ký tên muốn đưa ông Jensen Huang trở lại Nvidia");
        var en = AutoPostService.ProperNames("Megaspeed, firm at centre of Nvidia scandal, says Alice Huang left in 2025");
        vi.Intersect(en).Should().BeEquivalentTo(new[] { "nvidia", "huang" });
        AutoPostService.ProperNames("U23 Việt Nam vô địch giải Gia Lai VTV Cup").Should().BeEmpty();
    }

    [Fact]
    public void SameArticleWithTrackingParametersIsRecognisedAsUsed()
    {
        AutoPostService.NormalizeUrl("https://www.vnexpress.net/abc-123.html?utm_source=fb#comments")
            .Should().Be(AutoPostService.NormalizeUrl("https://vnexpress.net/abc-123.html"));
        AutoPostService.NormalizeUrl("https://site.com/story?id=1")
            .Should().NotBe(AutoPostService.NormalizeUrl("https://site.com/story?id=2"));
    }
}

public class NewsTopicTests
{
    [Theory]
    [InlineData("Đội tuyển Việt Nam thắng Thái Lan ở bán kết", "the-thao")]
    [InlineData("Giá vàng miếng hôm nay tăng mạnh", "kinh-doanh")]
    [InlineData("Apple ra mắt iPhone mới với chip nhanh hơn", "cong-nghe")]
    [InlineData("Công an khởi tố nhóm lừa đảo qua mạng", "phap-luat")]
    [InlineData("Hoa hậu Việt Nam 2026 lộ diện", "giai-tri")]
    [InlineData("Man City beat Arsenal in Premier League thriller", "the-thao")]
    public void HeadlinesGetTheirTopic(string title, string topic)
    {
        NewsTopics.Classify(title, null, null).Should().Contain(topic);
    }

    [Fact]
    public void AmbiguousVietnameseWordsDoNotTriggerTopics()
    {
        // "ai" = who, "thuê" = rent, "cụm" = cluster, "Nha Trang" = a city
        NewsTopics.Classify("Ai được thuê cụm nhà ở Nha Trang", null, null)
            .Should().NotContain(new[] { "cong-nghe", "kinh-doanh", "suc-khoe", "the-gioi" });
        NewsTopics.Classify("Startup dùng AI để chẩn đoán", null, null).Should().Contain("cong-nghe");
    }

    [Fact]
    public void SectionFeedTopicIsKeptEvenWithoutKeywords()
    {
        NewsTopics.Classify("Một ngày ở làng chài", null, "du-lich").Should().Contain("du-lich");
    }

    [Fact]
    public void CustomKeywordsIgnoreAccentsAndCase()
    {
        var keywords = NewsTopics.ParseKeywords("bóng đá Anh, Premier League ; crypto");
        keywords.Should().Equal("bóng đá Anh", "Premier League", "crypto");
        NewsTopics.MatchesKeywords("Lịch thi đấu BONG DA ANH cuối tuần", null, keywords).Should().BeTrue();
        NewsTopics.MatchesKeywords("Giá Bitcoin hôm nay", "thị trường crypto", keywords).Should().BeTrue();
        NewsTopics.MatchesKeywords("Thời tiết Hà Nội", null, keywords).Should().BeFalse();
    }
}

[Collection("DatabaseCollection")]
public class AutoPostApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AutoPostApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task EachPageHasItsOwnTopicAndScheduleAndShowsInTheOverview()
    {
        var sport = new FacebookAccount { PageId = "test-sport-" + Guid.NewGuid().ToString("N")[..8], PageName = "Test Thể Thao", EncryptedAccessToken = "x" };
        var tech = new FacebookAccount { PageId = "test-tech-" + Guid.NewGuid().ToString("N")[..8], PageName = "Test Công Nghệ", EncryptedAccessToken = "x" };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.FacebookAccounts.Add(sport);
            await db.SaveChangesAsync();
            tech.CreatedAtUtc = sport.CreatedAtUtc.AddSeconds(1);
            db.FacebookAccounts.Add(tech);
            await db.SaveChangesAsync();
        }

        try
        {
            // New pages get their own settings (off), and the second page's default times are shifted
            var overview = await _client.GetFromJsonAsync<AutoPostOverviewDto>("/api/auto-post");
            var sportPage = overview!.Pages.Single(p => p.FacebookAccountId == sport.Id);
            var techPage = overview.Pages.Single(p => p.FacebookAccountId == tech.Id);
            sportPage.Settings.Enabled.Should().BeFalse();
            techPage.Settings.TimeSlots.Should().NotEqual(sportPage.Settings.TimeSlots);

            var put = await _client.PutAsJsonAsync($"/api/auto-post/pages/{sport.Id}/settings", new AutoPostSettingsDto
            {
                Enabled = false,
                Topic = "the-thao",
                PostsPerDay = 3,
                TimeSlots = new List<string> { "08:00" }, // wrong count → suggested times
                RegionMode = "Mixed"
            });
            put.StatusCode.Should().Be(HttpStatusCode.OK);
            (await put.Content.ReadFromJsonAsync<AutoPostSettingsDto>())!.TimeSlots.Should().Equal(AutoPostService.SuggestSlots(3));

            put = await _client.PutAsJsonAsync($"/api/auto-post/pages/{tech.Id}/settings", new AutoPostSettingsDto
            {
                Enabled = false,
                Topic = "tu-chon",
                TopicKeywords = "chip, ChatGPT",
                PostsPerDay = 2,
                TimeSlots = new List<string> { "19:15", "06:45" },
                RegionMode = "World"
            });
            put.StatusCode.Should().Be(HttpStatusCode.OK);

            // A custom topic without keywords can't be switched on
            var bad = await _client.PutAsJsonAsync($"/api/auto-post/pages/{tech.Id}/settings", new AutoPostSettingsDto
            {
                Enabled = true, Topic = "tu-chon", TopicKeywords = " ", PostsPerDay = 2
            });
            bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            overview = await _client.GetFromJsonAsync<AutoPostOverviewDto>("/api/auto-post");
            sportPage = overview!.Pages.Single(p => p.FacebookAccountId == sport.Id);
            techPage = overview.Pages.Single(p => p.FacebookAccountId == tech.Id);
            sportPage.TopicName.Should().Be("Thể thao");
            sportPage.TodaySlots.Select(s => s.PlannedRegion).Should().Equal("VN", "World", "VN");
            sportPage.NextSlotUtc.Should().BeNull("auto-posting is switched off");
            techPage.Settings.Topic.Should().Be("tu-chon");
            techPage.Settings.TopicKeywords.Should().Be("chip, ChatGPT");
            techPage.TodaySlots.Select(s => s.Time).Should().Equal("06:45", "19:15");
            techPage.TodaySlots.Should().OnlyContain(s => s.PlannedRegion == "World");

            (await _client.PutAsJsonAsync($"/api/auto-post/pages/{Guid.NewGuid()}/settings", new AutoPostSettingsDto()))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var ids = new[] { sport.Id, tech.Id };
            db.AutoPostSettings.RemoveRange(await db.AutoPostSettings.Where(s => s.FacebookAccountId != null && ids.Contains(s.FacebookAccountId.Value)).ToListAsync());
            db.FacebookAccounts.RemoveRange(await db.FacebookAccounts.Where(a => ids.Contains(a.Id)).ToListAsync());
            await db.SaveChangesAsync();
        }
    }
}
