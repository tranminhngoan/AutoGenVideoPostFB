using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NewsStudio.Application.DTOs;
using NewsStudio.Infrastructure.Services;
using Xunit;

namespace NewsStudio.Tests;

public class AutoPostScheduleTests
{
    [Fact]
    public void DefaultFiveSlotsFollowVietnamPeakHours()
    {
        AutoPostService.SuggestSlots(5).Should().Equal("07:00", "11:30", "17:30", "20:00", "21:30");
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
    public void SameArticleWithTrackingParametersIsRecognisedAsUsed()
    {
        AutoPostService.NormalizeUrl("https://www.vnexpress.net/abc-123.html?utm_source=fb#comments")
            .Should().Be(AutoPostService.NormalizeUrl("https://vnexpress.net/abc-123.html"));
        AutoPostService.NormalizeUrl("https://site.com/story?id=1")
            .Should().NotBe(AutoPostService.NormalizeUrl("https://site.com/story?id=2"));
    }
}

[Collection("DatabaseCollection")]
public class AutoPostApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public AutoPostApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SettingsRoundTripAndTodaysPlanFollowThePostCount()
    {
        // A post count that doesn't match the given times gets suggested times instead
        var put = await _client.PutAsJsonAsync("/api/auto-post/settings", new AutoPostSettingsDto
        {
            Enabled = false,
            PostsPerDay = 3,
            TimeSlots = new List<string> { "08:00" },
            RegionMode = "Mixed"
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await put.Content.ReadFromJsonAsync<AutoPostSettingsDto>();
        saved!.TimeSlots.Should().Equal(AutoPostService.SuggestSlots(3));

        var overview = await _client.GetFromJsonAsync<AutoPostOverviewDto>("/api/auto-post");
        overview!.Settings.PostsPerDay.Should().Be(3);
        overview.TodaySlots.Select(s => s.Time).Should().Equal(AutoPostService.SuggestSlots(3));
        overview.TodaySlots.Select(s => s.PlannedRegion).Should().Equal("VN", "World", "VN");
        overview.NextSlotUtc.Should().BeNull("auto-posting is switched off");

        // Custom times are kept when they match the count
        put = await _client.PutAsJsonAsync("/api/auto-post/settings", new AutoPostSettingsDto
        {
            Enabled = false,
            PostsPerDay = 2,
            TimeSlots = new List<string> { "19:15", "06:45" },
            RegionMode = "World"
        });
        saved = await put.Content.ReadFromJsonAsync<AutoPostSettingsDto>();
        saved!.TimeSlots.Should().Equal("06:45", "19:15");
        saved.RegionMode.Should().Be("World");

        // Back to the defaults
        await _client.PutAsJsonAsync("/api/auto-post/settings", new AutoPostSettingsDto
        {
            Enabled = false,
            PostsPerDay = 5,
            TimeSlots = AutoPostService.SuggestSlots(5)
        });
    }
}
