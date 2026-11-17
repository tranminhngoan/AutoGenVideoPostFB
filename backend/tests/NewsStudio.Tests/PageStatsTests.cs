using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Facebook;
using Xunit;

namespace NewsStudio.Tests;

public class PageStatsScheduleTests
{
    [Fact]
    public void NewReelsAreReadMoreOftenThanOldOnes()
    {
        PageStatsService.RefreshInterval(TimeSpan.FromHours(5)).Should().Be(TimeSpan.FromHours(1));
        PageStatsService.RefreshInterval(TimeSpan.FromDays(3)).Should().Be(TimeSpan.FromHours(3));
        PageStatsService.RefreshInterval(TimeSpan.FromDays(10)).Should().Be(TimeSpan.FromHours(12));
        PageStatsService.RefreshInterval(TimeSpan.FromDays(60)).Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void VietnamDayStartsAtFivePmUtc()
    {
        FacebookPublishingService.VietnamDayStartUtc(new DateTime(2026, 11, 16, 3, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2026, 11, 15, 17, 0, 0));
        FacebookPublishingService.VietnamDayStartUtc(new DateTime(2026, 11, 16, 18, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2026, 11, 16, 17, 0, 0));
    }
}

[Collection("DatabaseCollection")]
public class PageStatsApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PageStatsApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ViewsAreSummedPerPageAndSplitByDay()
    {
        var page = new FacebookAccount { PageId = "test-stats-" + Guid.NewGuid().ToString("N")[..8], PageName = "Test Thống Kê", EncryptedAccessToken = "x", InstagramUsername = "test.thongke" };
        var project = new VideoProject { Title = "Video thử thống kê", SourceUrl = "https://example.com/" + Guid.NewGuid().ToString("N") };
        // Snapshots in the middle of Vietnam days, so the day each falls on is unambiguous
        var todayNoonUtc = FacebookPublishingService.VietnamDayStartUtc(DateTime.UtcNow).AddHours(12);
        if (todayNoonUtc > DateTime.UtcNow) todayNoonUtc = DateTime.UtcNow.AddMinutes(-1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.FacebookAccounts.Add(page);
            db.VideoProjects.Add(project);
            await db.SaveChangesAsync();
            var post = new FacebookPost
            {
                VideoProjectId = project.Id,
                FacebookAccountId = page.Id,
                FacebookVideoId = "123456789",
                FacebookPostId = "1_123456789",
                Status = "Published",
                UploadStatus = "Published",
                PublishedAtUtc = todayNoonUtc.AddDays(-3),
                InstagramMediaId = "1789000000000"
            };
            db.FacebookPosts.Add(post);
            await db.SaveChangesAsync();
            db.VideoMetrics.AddRange(
                new VideoMetric { FacebookPostId = post.Id, Status = "Initialized", Views = 0, CollectedAtUtc = todayNoonUtc.AddDays(-3) },
                new VideoMetric { FacebookPostId = post.Id, Status = "Recorded", Views = 100, Reach = 80, CollectedAtUtc = todayNoonUtc.AddDays(-2) },
                new VideoMetric { FacebookPostId = post.Id, Status = "Recorded", Views = 250, Reach = 190, CollectedAtUtc = todayNoonUtc.AddDays(-1) },
                new VideoMetric { FacebookPostId = post.Id, Status = "Recorded", Views = 400, Reach = 300, Engagement = 12, CollectedAtUtc = todayNoonUtc });
            db.InstagramMetrics.AddRange(
                new InstagramMetric { FacebookPostId = post.Id, Views = 30, Likes = 2, CollectedAtUtc = todayNoonUtc.AddDays(-1) },
                new InstagramMetric { FacebookPostId = post.Id, Views = 75, Likes = 5, Comments = 1, CollectedAtUtc = todayNoonUtc });
            await db.SaveChangesAsync();
        }

        try
        {
            var stats = await _client.GetFromJsonAsync<PageStatsOverviewDto>("/api/facebook/page-stats?days=14");
            stats!.Dates.Should().HaveCount(14);
            var p = stats.Pages.Single(x => x.FacebookAccountId == page.Id);
            p.Videos.Should().Be(1);
            p.VideosWithStats.Should().Be(1);
            p.TotalViews.Should().Be(400);
            p.ViewsToday.Should().Be(150);
            p.Views7Days.Should().Be(400);
            p.Reach.Should().Be(300);
            p.Likes.Should().Be(12);
            p.DailyViews.TakeLast(3).Should().Equal(100, 150, 150);
            p.TopVideo!.Views.Should().Be(400);
            p.TopVideo.Title.Should().Be("Video thử thống kê");
            p.TopVideo.PostUrl.Should().Be("https://www.facebook.com/reel/123456789");
            stats.Totals.TotalViews.Should().BeGreaterThanOrEqualTo(400);

            // Instagram copy of the Reel: its own numbers and day-by-day gains
            p.InstagramUsername.Should().Be("test.thongke");
            p.InstagramVideos.Should().Be(1);
            p.InstagramViews.Should().Be(75);
            p.InstagramViewsToday.Should().Be(45);
            p.InstagramViews7Days.Should().Be(75);
            p.InstagramLikes.Should().Be(5);
            p.InstagramDailyViews.TakeLast(2).Should().Equal(30, 45);
            stats.Totals.InstagramViews.Should().BeGreaterThanOrEqualTo(75);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var posts = await db.FacebookPosts.Where(x => x.FacebookAccountId == page.Id).ToListAsync();
            var postIds = posts.Select(x => x.Id).ToList();
            db.VideoMetrics.RemoveRange(await db.VideoMetrics.Where(m => postIds.Contains(m.FacebookPostId)).ToListAsync());
            db.InstagramMetrics.RemoveRange(await db.InstagramMetrics.Where(m => postIds.Contains(m.FacebookPostId)).ToListAsync());
            db.FacebookPosts.RemoveRange(posts);
            db.VideoProjects.RemoveRange(await db.VideoProjects.Where(x => x.Id == project.Id).ToListAsync());
            db.FacebookAccounts.RemoveRange(await db.FacebookAccounts.Where(a => a.Id == page.Id).ToListAsync());
            await db.SaveChangesAsync();
        }
    }
}
