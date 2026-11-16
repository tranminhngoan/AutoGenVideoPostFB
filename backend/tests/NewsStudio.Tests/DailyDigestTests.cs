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
using NewsStudio.Infrastructure.Services;
using Xunit;

namespace NewsStudio.Tests;

public class DailyDigestRulesTests
{
    [Fact]
    public void PicksAreReadInOrderWithoutRepeatsOrUnknownNumbers()
    {
        DailyDigestService.ParsePicks("{\"picks\": [4, 2, 4, 99, 0, \"7\"]}", 10).Should().Equal(3, 1, 6);
        DailyDigestService.ParsePicks("```json\n[1, 3]\n```", 5).Should().Equal(0, 2);
        DailyDigestService.ParsePicks("{\"other\": 1}", 5).Should().BeEmpty();
    }

    [Fact]
    public void WrittenStoriesAreMatchedByTheirNumber()
    {
        var written = DailyDigestService.ParseWritten(
            "{\"items\": [{\"id\": 3, \"headline\": \"  Tuyển Việt Nam thua sốc \", \"summary\": \"Thua 1-2 trên sân nhà.\"}]}");
        written[3].Should().Be(("Tuyển Việt Nam thua sốc", "Thua 1-2 trên sân nhà."));

        // Without ids, the order of the list is the story number
        var byPosition = DailyDigestService.ParseWritten("[{\"headline\": \"A\", \"summary\": \"a\"}, {\"headline\": \"B\", \"summary\": \"b\"}]");
        byPosition[2].Headline.Should().Be("B");
    }

    [Fact]
    public void WallPostTextListsEveryStoryWithItsSource()
    {
        var text = DailyDigestService.BuildCaption("the-thao", new DateTime(2026, 11, 16), new List<DailyDigestItemDto>
        {
            new() { Headline = "Messi lập cú đúp", Summary = "Inter Miami vào chung kết.", SourceName = "VnExpress" },
            new() { Headline = "Tuyển Việt Nam thua sốc", Summary = "Thua 1-2 trên sân nhà.", SourceName = "Tuổi Trẻ" },
        });

        text.Should().StartWith("🔥 ĐIỂM TIN NÓNG THỂ THAO NGÀY 16/11/2026");
        text.Should().Contain("1️⃣ Messi lập cú đúp\nInter Miami vào chung kết.\n(Nguồn: VnExpress)");
        text.Should().Contain("2️⃣ Tuyển Việt Nam thua sốc");
        text.Should().EndWith("#DiemTinNong #TinTuc");
    }

    [Fact]
    public void StoryOfTheLargestDigestStaysUnderOneMinute()
    {
        (DailyDigestService.CoverSeconds + DailyDigestService.MaxItems * DailyDigestService.ItemSeconds).Should().BeLessThan(60);
    }
}

[Collection("DatabaseCollection")]
public class DailyDigestApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public DailyDigestApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DigestSettingsAreSavedPerPageAndAFailedDigestSaysWhy()
    {
        var page = new FacebookAccount { PageId = "test-digest-" + Guid.NewGuid().ToString("N")[..8], PageName = "Test Bản Tin", EncryptedAccessToken = "x" };
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
            settings.DigestEnabled.Should().BeTrue();
            settings.DigestTime.Should().Be("20:30");
            settings.DigestItems.Should().Be(6);

            // Custom topic without keywords: the digest can't choose stories and says so
            settings.Topic = "tu-chon";
            settings.TopicKeywords = "";
            settings.DigestTime = "21:05";
            settings.DigestItems = 20; // capped
            var put = await _client.PutAsJsonAsync($"/api/auto-post/pages/{page.Id}/settings", settings);
            put.StatusCode.Should().Be(HttpStatusCode.OK);
            var saved = await put.Content.ReadFromJsonAsync<AutoPostSettingsDto>();
            saved!.DigestTime.Should().Be("21:05");
            saved.DigestItems.Should().Be(DailyDigestService.MaxItems);

            settings.DigestTime = "giờ vàng"; // not a time → default
            saved = await (await _client.PutAsJsonAsync($"/api/auto-post/pages/{page.Id}/settings", settings)).Content.ReadFromJsonAsync<AutoPostSettingsDto>();
            saved!.DigestTime.Should().Be("20:30");

            var preview = await _client.PostAsync($"/api/daily-digest/pages/{page.Id}/preview", null);
            preview.StatusCode.Should().Be(HttpStatusCode.OK);
            var run = await preview.Content.ReadFromJsonAsync<DailyDigestRunDto>();
            run!.Status.Should().Be("Failed");
            run.IsPreview.Should().BeTrue();
            run.Message.Should().Contain("chưa có từ khoá");

            var recent = await _client.GetFromJsonAsync<List<DailyDigestRunDto>>($"/api/daily-digest?pageId={page.Id}");
            recent!.Should().ContainSingle(r => r.Id == run.Id);

            (await _client.GetAsync($"/api/daily-digest/runs/{run.Id}/images/..%2Fsecret.jpg")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await _client.GetAsync($"/api/daily-digest/runs/{run.Id}/images/post_00.jpg")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await _client.PostAsync($"/api/daily-digest/pages/{Guid.NewGuid()}/preview", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.DailyDigestRuns.RemoveRange(await db.DailyDigestRuns.Where(r => r.FacebookAccountId == page.Id).ToListAsync());
            db.AutoPostSettings.RemoveRange(await db.AutoPostSettings.Where(s => s.FacebookAccountId == page.Id).ToListAsync());
            db.FacebookAccounts.RemoveRange(await db.FacebookAccounts.Where(a => a.Id == page.Id).ToListAsync());
            await db.SaveChangesAsync();
        }
    }
}
