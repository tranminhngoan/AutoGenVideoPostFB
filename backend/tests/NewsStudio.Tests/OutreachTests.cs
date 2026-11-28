using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Outreach;
using Xunit;

namespace NewsStudio.Tests;

public class OutreachLogicTests
{
    [Theory]
    [InlineData("https://www.facebook.com/groups/abc.def/posts/123?ref=x", "https://www.facebook.com/groups/abc.def")]
    [InlineData("m.facebook.com/groups/123456789/", "https://www.facebook.com/groups/123456789")]
    [InlineData("https://web.facebook.com/groups/TuongTacCheo", "https://www.facebook.com/groups/tuongtaccheo")]
    [InlineData("https://www.facebook.com/somepage", null)]
    [InlineData("https://evil.com/groups/abc", null)]
    [InlineData("", null)]
    public void GroupLinksAreNormalised(string input, string? expected) =>
        OutreachService.NormalizeGroupUrl(input).Should().Be(expected);

    [Fact]
    public void PasswordsAreHashedAndVerified()
    {
        var hash = OutreachAuth.HashPassword("Mat-khau-dai-123");
        hash.Should().StartWith("pbkdf2-sha256$").And.NotContain("Mat-khau");
        OutreachAuth.VerifyPassword("Mat-khau-dai-123", hash).Should().BeTrue();
        OutreachAuth.VerifyPassword("mat-khau-dai-123", hash).Should().BeFalse();
        OutreachAuth.VerifyPassword("", hash).Should().BeFalse();
        OutreachAuth.VerifyPassword("x", "garbage").Should().BeFalse();
    }

    [Fact]
    public void NearDuplicateTextsAreSpotted()
    {
        var a = "Mọi người ghé ủng hộ Page của mình nhé ❤️ Mình đang xây dựng Page nên rất mong được Follow. 👉 https://x.vn/a";
        var b = "Mọi người ghé ủng hộ Page của mình nhé! Mình đang xây dựng Page nên rất mong được Follow 👉 https://x.vn/b?utm=1";
        OutreachService.Similarity(a, b).Should().BeGreaterThan(80);
        OutreachService.Similarity(a, "Hôm nay trời đẹp quá, đi cà phê không cả nhà?").Should().BeLessThan(15);
    }

    [Theory]
    [InlineData("Nhóm Công khai · 12K thành viên · 10+ bài viết mỗi ngày", 12_000)]
    [InlineData("Private group · 3.4K members", 3_400)]
    [InlineData("1,2 N thành viên", 1_200)]
    [InlineData("12.345 thành viên", 12_345)]
    [InlineData("2,5 triệu thành viên", 2_500_000)]
    [InlineData("Không có số", null)]
    public void MemberCountsAreReadFromSnippets(string text, int? expected) =>
        OutreachService.ParseMembers(text).Should().Be(expected);

    [Fact]
    public void BraveResultsKeepOnlyGroupLinks()
    {
        using var doc = JsonDocument.Parse("""
        {"web":{"results":[
          {"title":"Hội Tương Tác Chéo Uy Tín | Facebook","url":"https://www.facebook.com/groups/tuongtaccheo/","description":"Nhóm Công khai · <strong>25K</strong> thành viên","extra_snippets":["Follow chéo mỗi ngày"]},
          {"title":"Some Page | Facebook","url":"https://www.facebook.com/somepage","description":"A page"},
          {"title":"Ký ức 8x 9x - Facebook","url":"https://m.facebook.com/groups/123456789/posts/987","description":"Private group · 3.4K members"},
          {"title":"dup","url":"https://web.facebook.com/groups/tuongtaccheo","description":""}
        ]}}
        """);
        var list = OutreachService.ParseBraveResults(doc.RootElement);
        list.Should().HaveCount(2);
        list[0].Name.Should().Be("Hội Tương Tác Chéo Uy Tín");
        list[0].Url.Should().Be("https://www.facebook.com/groups/tuongtaccheo");
        list[0].Members.Should().Be(25_000);
        list[0].Privacy.Should().Be("public");
        list[1].Name.Should().Be("Ký ức 8x 9x");
        list[1].Url.Should().Be("https://www.facebook.com/groups/123456789");
        list[1].Privacy.Should().Be("private");
    }
}

/// <summary>Campaign menu: locked behind its own password, then the whole flow works.</summary>
public class OutreachApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Password = "Test-chien-dich-2026";
    private readonly WebApplicationFactory<Program> _factory;

    public OutreachApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b => b
            .UseSetting("Outreach:PasswordHash", OutreachAuth.HashPassword(Password))
            // test Pages have random ids: allow any connected Page except the main one as a helper
            .UseSetting("Outreach:HelperPageIds", "")
            .UseSetting("Outreach:BraveSearch:ApiKey", ""));
    }

    private async Task ResetSecurityAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        db.OutreachSettings.RemoveRange(await db.OutreachSettings.ToListAsync());
        db.OutreachUnlockAttempts.RemoveRange(await db.OutreachUnlockAttempts.ToListAsync());
        await db.SaveChangesAsync();
    }

    private HttpClient Client(string ip)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        return c;
    }

    private async Task<HttpClient> UnlockedAsync(string ip)
    {
        var c = Client(ip);
        var res = await c.PostAsJsonAsync("/api/outreach/auth/unlock", new OutreachUnlockRequestDto { Password = Password });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<OutreachUnlockResultDto>();
        c.DefaultRequestHeaders.Add(OutreachAuth.SessionHeader, body!.Token);
        return c;
    }

    [Fact]
    public async Task MenuIsLockedUntilTheCampaignPasswordIsGivenAndWrongTriesLockTheAddress()
    {
        await ResetSecurityAsync();
        var anon = Client("10.0.0.1");

        // No session → nothing
        foreach (var url in new[] { "/api/outreach/overview", "/api/outreach/groups", "/api/outreach/campaigns", "/api/outreach/security", "/api/outreach/stats" })
            (await anon.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);
        (await anon.PostAsJsonAsync("/api/outreach/groups", new { name = "x", url = "facebook.com/groups/abc" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Forged / tampered tokens are refused
        anon.DefaultRequestHeaders.Add(OutreachAuth.SessionHeader, $"{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}.1.deadbeef");
        (await anon.GetAsync("/api/outreach/groups")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Status needs no session and never leaks the hash
        var status = await (await Client("10.0.0.1").GetAsync("/api/outreach/auth/status")).Content.ReadAsStringAsync();
        status.Should().Contain("\"passwordConfigured\":true").And.NotContain("pbkdf2");

        // Wrong password: counted, then the address is locked — even for the right password
        var attacker = Client("10.0.0.66");
        for (var i = 1; i <= OutreachAuth.MaxAttempts; i++)
        {
            var res = await attacker.PostAsJsonAsync("/api/outreach/auth/unlock", new OutreachUnlockRequestDto { Password = "sai-" + i });
            var body = await res.Content.ReadFromJsonAsync<OutreachUnlockResultDto>();
            body!.Ok.Should().BeFalse();
            body.Token.Should().BeNull();
            if (i < OutreachAuth.MaxAttempts) { res.StatusCode.Should().Be(HttpStatusCode.Unauthorized); body.AttemptsLeft.Should().Be(OutreachAuth.MaxAttempts - i); }
            else res.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        }
        var locked = await attacker.PostAsJsonAsync("/api/outreach/auth/unlock", new OutreachUnlockRequestDto { Password = Password });
        locked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await locked.Content.ReadFromJsonAsync<OutreachUnlockResultDto>())!.LockedUntilUtc.Should().BeAfter(DateTime.UtcNow);

        // Another address with the right password gets in
        var ok = await UnlockedAsync("10.0.0.2");
        (await ok.GetAsync("/api/outreach/groups")).StatusCode.Should().Be(HttpStatusCode.OK);

        // The security log shows the tries; changing the password ends old sessions
        var sec = await ok.GetFromJsonAsync<OutreachSecurityStatusDto>("/api/outreach/security");
        sec!.RecentAttempts.Should().Contain(a => a.Ip == "10.0.0.66" && !a.Success).And.Contain(a => a.Ip == "10.0.0.2" && a.Success);
        (await ok.PutAsJsonAsync("/api/outreach/security", new OutreachChangePasswordDto { CurrentPassword = "sai", NewPassword = "Moi-mat-khau-999" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ok.PutAsJsonAsync("/api/outreach/security", new OutreachChangePasswordDto { CurrentPassword = Password, NewPassword = "ngan1" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ok.PutAsJsonAsync("/api/outreach/security", new OutreachChangePasswordDto { CurrentPassword = Password, NewPassword = "Moi-mat-khau-999" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ok.GetAsync("/api/outreach/groups")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Client("10.0.0.3").PostAsJsonAsync("/api/outreach/auth/unlock", new OutreachUnlockRequestDto { Password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var fresh = Client("10.0.0.3");
        var unlock = await (await fresh.PostAsJsonAsync("/api/outreach/auth/unlock", new OutreachUnlockRequestDto { Password = "Moi-mat-khau-999" }))
            .Content.ReadFromJsonAsync<OutreachUnlockResultDto>();
        unlock!.Ok.Should().BeTrue();
        fresh.DefaultRequestHeaders.Add(OutreachAuth.SessionHeader, unlock.Token);
        (await fresh.GetAsync("/api/outreach/groups")).StatusCode.Should().Be(HttpStatusCode.OK);

        // "Lock everywhere" ends this session too
        (await fresh.PostAsync("/api/outreach/security/lock-all", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await fresh.GetAsync("/api/outreach/groups")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await ResetSecurityAsync();
    }

    [Fact]
    public async Task WholeCampaignFlowFromGroupsToTrackedClicks()
    {
        await ResetSecurityAsync();
        var c = await UnlockedAsync("10.0.1.1");
        var tag = Guid.NewGuid().ToString("N")[..6];
        var pageA = new FacebookAccount { PageId = "8" + Random.Shared.NextInt64(100_000_000, 999_999_999), PageName = "Trạm Tin Showbiz " + tag, EncryptedAccessToken = "x" };
        var pageB = new FacebookAccount { PageId = "8" + Random.Shared.NextInt64(100_000_000, 999_999_999), PageName = "Trạm Tin Công Nghệ " + tag, EncryptedAccessToken = "x" };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.FacebookAccounts.AddRange(pageA, pageB);
            await db.SaveChangesAsync();
        }
        var createdGroups = new List<Guid>();
        Guid? campaignId = null;
        string? slug = null;
        try
        {
            // ── Groups: add, duplicate refused, bad link refused, edit, CSV import ──
            var g1 = await (await c.PostAsJsonAsync("/api/outreach/groups", new OutreachGroupInputDto
            {
                Name = "Hội tương tác chéo " + tag, Url = $"https://m.facebook.com/groups/test{tag}a/?ref=share", Topic = "tương tác",
                ExchangeTypes = new() { "follow", "like", "bogus" }, Fit = "fit",
            })).Content.ReadFromJsonAsync<OutreachGroupDto>();
            createdGroups.Add(g1!.Id);
            g1.Url.Should().Be($"https://www.facebook.com/groups/test{tag}a");
            g1.ExchangeTypes.Should().Equal("follow", "like");
            g1.Key.Should().MatchRegex("^g[0-9]{2,}$");
            (await c.PostAsJsonAsync("/api/outreach/groups", new OutreachGroupInputDto { Name = "dup", Url = $"facebook.com/groups/test{tag}a" }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await c.PostAsJsonAsync("/api/outreach/groups", new OutreachGroupInputDto { Name = "bad", Url = "https://example.com/x" }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var edited = await (await c.PutAsJsonAsync($"/api/outreach/groups/{g1.Id}", new OutreachGroupInputDto
            {
                Name = g1.Name, Topic = "hoài niệm", ExchangeTypes = new() { "comment" }, Fit = "unfit", Rules = "Không đăng link",
            })).Content.ReadFromJsonAsync<OutreachGroupDto>();
            edited!.Fit.Should().Be("unfit");
            edited.Rules.Should().Be("Không đăng link");

            var csv = $"url,ten,chu_de,loai\nhttps://www.facebook.com/groups/test{tag}b,Nhóm B,giải trí,follow;share\nkhong-phai-link,X,,\nfacebook.com/groups/test{tag}a,trùng,,";
            var import = await (await c.PostAsJsonAsync("/api/outreach/groups/import", new { csv })).Content.ReadFromJsonAsync<OutreachImportResultDto>();
            import!.Added.Should().Be(1);
            import.Duplicates.Should().Be(1);
            import.Invalid.Should().HaveCount(1);
            var groups = await c.GetFromJsonAsync<List<OutreachGroupDto>>("/api/outreach/groups");
            var g2 = groups!.Single(g => g.Url == $"https://www.facebook.com/groups/test{tag}b");
            createdGroups.Add(g2.Id);
            g2.Name.Should().Be("Nhóm B");
            g2.ExchangeTypes.Should().Equal("follow", "share");

            var search = await c.GetFromJsonAsync<OutreachGroupSearchDto>("/api/outreach/groups/search?keyword=" + Uri.EscapeDataString("hoài niệm"));
            search!.FacebookSearchUrl.Should().StartWith("https://www.facebook.com/search/groups/?q=");

            // ── Content library: save, near-duplicate refused, copy counter ──
            var text = $"Mọi người ghé ủng hộ Page của mình nhé {tag} ❤️ Mình đang xây dựng Page nên rất mong được mọi người Follow và xem video.";
            var content = await (await c.PostAsJsonAsync("/api/outreach/contents", new OutreachContentInputDto { Text = text, Style = "friendly" }))
                .Content.ReadFromJsonAsync<OutreachContentDto>();
            (await c.PostAsJsonAsync("/api/outreach/contents", new OutreachContentInputDto { Text = text + "!" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var check = await (await c.PostAsJsonAsync("/api/outreach/contents/check", new OutreachTextCheckDto { Text = text })).Content.ReadFromJsonAsync<OutreachDraftDto>();
            check!.Similarity.Should().BeGreaterThanOrEqualTo(90);
            (await c.PostAsync($"/api/outreach/contents/{content!.Id}/copied", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

            // ── Campaign: helper pages only, links per Group × Page ──
            var campaign = await (await c.PostAsJsonAsync("/api/outreach/campaigns", new OutreachCampaignInputDto
            {
                Name = "Thử " + tag, Slug = "test_" + tag, PageIds = new() { pageA.Id, pageB.Id }, GroupIds = new() { g1.Id, g2.Id },
                ContentId = content.Id, ContentText = text, Status = "Running", Owner = "Admin",
                StartDate = DateTime.UtcNow.Date, EndDate = DateTime.UtcNow.Date.AddDays(7),
            })).Content.ReadFromJsonAsync<OutreachCampaignDto>();
            campaignId = campaign!.Id;
            slug = campaign.Slug;
            campaign.Status.Should().Be("Running");
            campaign.ActivitiesPlanned.Should().Be(4);
            (await c.PostAsJsonAsync("/api/outreach/campaigns", new OutreachCampaignInputDto { Name = "Sai ngày", StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddDays(-2) }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // Exactly what the browser's date pickers send: "yyyy-MM-dd" with no time zone
            var fromForm = await c.PostAsync("/api/outreach/campaigns", new StringContent(
                $"{{\"name\":\"Ngày từ form {tag}\",\"status\":\"Draft\",\"pageIds\":[\"{pageA.Id}\"],\"groupIds\":[\"{g1.Id}\"],\"owner\":\"NgoanTM\",\"startDate\":\"2026-11-28\",\"endDate\":\"2027-10-28\",\"contentId\":null,\"contentText\":\"abc\"}}",
                System.Text.Encoding.UTF8, "application/json"));
            fromForm.StatusCode.Should().Be(HttpStatusCode.OK, await fromForm.Content.ReadAsStringAsync());
            var formCampaign = await fromForm.Content.ReadFromJsonAsync<OutreachCampaignDto>();
            formCampaign!.StartDate!.Value.ToString("yyyy-MM-dd").Should().Be("2026-11-28");
            formCampaign.EndDate!.Value.ToString("yyyy-MM-dd").Should().Be("2027-10-28");
            (await c.DeleteAsync($"/api/outreach/campaigns/{formCampaign.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

            var links = await c.GetFromJsonAsync<List<OutreachCampaignLinkDto>>($"/api/outreach/campaigns/{campaignId}/links");
            links.Should().HaveCount(4);
            var link = links!.First(l => l.GroupId == g1.Id && l.PageId == pageA.Id);
            link.Link.Should().Contain("/cheo-uy-tinh?utm_source=facebook&utm_medium=group&utm_campaign=test_" + tag)
                .And.Contain("utm_content=" + link.UtmContent);
            link.UtmContent.Should().EndWith("_" + g1.Key).And.StartWith("showbiz");

            // Done by hand: mark posted
            (await c.PostAsJsonAsync($"/api/outreach/campaigns/{campaignId}/activities", new OutreachActivityInputDto
            {
                GroupId = g1.Id, PageId = pageA.Id, Kind = "post", PostUrl = "https://www.facebook.com/groups/x/posts/1",
            })).StatusCode.Should().Be(HttpStatusCode.OK);
            (await c.PostAsJsonAsync($"/api/outreach/campaigns/{campaignId}/activities", new OutreachActivityInputDto { GroupId = Guid.NewGuid() }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // ── A visitor arrives through that link and clicks the button (public, no session) ──
            var visitor = _factory.CreateClient();
            foreach (var type in new[] { "view", "open_page" })
                (await visitor.PostAsJsonAsync("/api/public/landing/events", new LandingEventRequestDto
                {
                    Slug = "cheo-uy-tinh", Type = type, Device = "mobile", VisitorId = "v-" + tag,
                    UtmSource = "facebook", UtmMedium = "group", UtmCampaign = slug, UtmContent = link.UtmContent,
                })).StatusCode.Should().Be(HttpStatusCode.NoContent);

            var after = (await c.GetFromJsonAsync<List<OutreachCampaignLinkDto>>($"/api/outreach/campaigns/{campaignId}/links"))!
                .Single(l => l.UtmContent == link.UtmContent);
            after.Views.Should().Be(1);
            after.Clicks.Should().Be(1);
            after.Activities.Should().ContainSingle(a => a.Kind == "post");
            var listed = (await c.GetFromJsonAsync<List<OutreachCampaignDto>>("/api/outreach/campaigns"))!.Single(x => x.Id == campaignId);
            // counted over the campaign's dates (posts carry no link): at least this visitor's view + click
            listed.Views.Should().BeGreaterThanOrEqualTo(1);
            listed.Clicks.Should().BeGreaterThanOrEqualTo(1);
            listed.TodayClicks.Should().BeGreaterThanOrEqualTo(1);
            listed.Visitors.Should().BeGreaterThanOrEqualTo(1);
            listed.ActivitiesDone.Should().Be(1);
            (await c.GetFromJsonAsync<List<OutreachGroupDto>>("/api/outreach/groups"))!.Single(g => g.Id == g1.Id).Clicks.Should().Be(1);
            var stats = await c.GetFromJsonAsync<OutreachStatsDto>("/api/outreach/stats?days=7");
            stats!.PerDay.Should().HaveCount(7);
            stats.Campaigns.Should().Contain(x => x.Key == slug && x.Views >= 1 && x.Clicks >= 1 && x.Label == "Thử " + tag);
            stats.Contents.Should().Contain(x => x.Key == link.UtmContent && x.Label!.Contains(g1.Name));

            // ── Per-Page membership of a Group (set by hand) ──
            var joined = await (await c.PutAsJsonAsync($"/api/outreach/groups/{g1.Id}/membership/{pageA.Id}", new OutreachMembershipInputDto { Status = "joined" }))
                .Content.ReadFromJsonAsync<OutreachMembershipDto>();
            joined!.Status.Should().Be("joined");
            (await c.PutAsJsonAsync($"/api/outreach/groups/{g1.Id}/membership/{pageB.Id}", new OutreachMembershipInputDto { Status = "requested" }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            var g1Now = (await c.GetFromJsonAsync<List<OutreachGroupDto>>("/api/outreach/groups"))!.Single(g => g.Id == g1.Id);
            g1Now.Memberships.Should().HaveCount(2).And.Contain(m => m.PageId == pageA.Id && m.Status == "joined");
            var linkRows = await c.GetFromJsonAsync<List<OutreachCampaignLinkDto>>($"/api/outreach/campaigns/{campaignId}/links");
            linkRows!.Single(l => l.GroupId == g1.Id && l.PageId == pageA.Id).Membership.Should().Be("joined");
            linkRows!.Single(l => l.GroupId == g2.Id && l.PageId == pageA.Id).Membership.Should().BeNull();
            (await c.PutAsJsonAsync($"/api/outreach/groups/{g1.Id}/membership/{pageB.Id}", new OutreachMembershipInputDto { Status = "none" }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await c.GetFromJsonAsync<List<OutreachGroupDto>>("/api/outreach/groups"))!.Single(g => g.Id == g1.Id).Memberships.Should().ContainSingle();
            (await c.PutAsJsonAsync($"/api/outreach/groups/{g1.Id}/membership/{Guid.NewGuid()}", new OutreachMembershipInputDto { Status = "joined" }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // ── Wall post on a helper Page, then shared into a Group by hand (ticked in the tool) ──
            Guid wallId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var wall = new OutreachWallPost { FacebookAccountId = pageA.Id, CampaignId = campaignId, ExternalPostId = pageA.PageId + "_1", PermalinkUrl = "https://www.facebook.com/x", Text = "Bài thử " + tag };
                db.OutreachWallPosts.Add(wall);
                await db.SaveChangesAsync();
                wallId = wall.Id;
            }
            var walls = await c.GetFromJsonAsync<List<OutreachWallPostDto>>($"/api/outreach/wall-posts?pageId={pageA.Id}");
            walls!.Should().ContainSingle(w => w.Id == wallId && w.PageName == pageA.PageName);
            (await c.PutAsJsonAsync($"/api/outreach/wall-posts/{wallId}/shares/{g1.Id}", new OutreachShareInputDto { Shared = true })).StatusCode.Should().Be(HttpStatusCode.OK);
            (await c.PutAsJsonAsync($"/api/outreach/wall-posts/{wallId}/shares/{g1.Id}", new OutreachShareInputDto { Shared = true })).StatusCode.Should().Be(HttpStatusCode.OK); // idempotent
            (await c.GetFromJsonAsync<List<OutreachWallPostDto>>($"/api/outreach/wall-posts?pageId={pageA.Id}"))!.Single(w => w.Id == wallId).Shares.Should().ContainSingle(s => s.GroupId == g1.Id);
            var sharedRow = (await c.GetFromJsonAsync<List<OutreachCampaignLinkDto>>($"/api/outreach/campaigns/{campaignId}/links"))!.Single(l => l.GroupId == g1.Id && l.PageId == pageA.Id);
            sharedRow.Shares.Should().Be(1);
            sharedRow.LastSharedAtUtc.Should().NotBeNull();
            (await c.GetFromJsonAsync<List<OutreachCampaignDto>>("/api/outreach/campaigns"))!.Single(x => x.Id == campaignId).ActivitiesDone.Should().Be(2); // 1 old mark + 1 share
            (await c.PutAsJsonAsync($"/api/outreach/wall-posts/{wallId}/shares/{g1.Id}", new OutreachShareInputDto { Shared = false })).StatusCode.Should().Be(HttpStatusCode.OK);
            (await c.GetFromJsonAsync<List<OutreachWallPostDto>>($"/api/outreach/wall-posts?pageId={pageA.Id}"))!.Single(w => w.Id == wallId).Shares.Should().BeEmpty();
            // Publishing checks its input before calling Facebook
            using (var form = new MultipartFormDataContent { { new StringContent(Guid.NewGuid().ToString()), "pageId" }, { new StringContent("x"), "message" }, { new ByteArrayContent(new byte[] { 1, 2, 3 }), "image", "a.jpg" } })
                (await c.PostAsync("/api/outreach/wall-posts", form)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using (var form = new MultipartFormDataContent { { new StringContent(pageA.Id.ToString()), "pageId" }, { new StringContent("x"), "message" } })
                (await c.PostAsync("/api/outreach/wall-posts", form)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // Group search needs the Brave key — a clear message, not a crash
            var noKey = await c.GetAsync("/api/outreach/groups/discover?keyword=" + Uri.EscapeDataString("hoài niệm"));
            noKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await noKey.Content.ReadAsStringAsync()).Should().Contain("Brave");

            // ── Group post to answer by hand ──
            var task = await (await c.PostAsJsonAsync("/api/outreach/posts", new OutreachPostTaskInputDto
            {
                GroupId = g1.Id, PostUrl = "https://www.facebook.com/groups/x/posts/2", PostText = "Ai còn nhớ kẹo kéo ngày xưa?",
            })).Content.ReadFromJsonAsync<OutreachPostTaskDto>();
            task!.Status.Should().Be("todo");
            task.GroupName.Should().Be(g1.Name);
            (await c.PostAsJsonAsync("/api/outreach/posts", new OutreachPostTaskInputDto { PostUrl = "https://example.com" }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var doneTask = await (await c.PutAsJsonAsync($"/api/outreach/posts/{task.Id}", new OutreachPostTaskInputDto
            {
                GroupId = g1.Id, PostUrl = task.PostUrl, PostText = task.PostText, SuggestedComment = "Nhớ chứ!", Status = "done",
            })).Content.ReadFromJsonAsync<OutreachPostTaskDto>();
            doneTask!.Status.Should().Be("done");
            doneTask.DoneAtUtc.Should().NotBeNull();
            (await c.DeleteAsync($"/api/outreach/posts/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

            // ── Edit campaign status, delete campaign / content ──
            var paused = await (await c.PutAsJsonAsync($"/api/outreach/campaigns/{campaignId}", new OutreachCampaignInputDto
            {
                Name = campaign.Name, PageIds = new() { pageA.Id }, GroupIds = new() { g1.Id }, Status = "Paused", ResultNote = "Tạm dừng",
            })).Content.ReadFromJsonAsync<OutreachCampaignDto>();
            paused!.Status.Should().Be("Paused");
            paused.ActivitiesPlanned.Should().Be(1);
            (await c.DeleteAsync($"/api/outreach/contents/{content.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await c.DeleteAsync($"/api/outreach/campaigns/{campaignId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            campaignId = null;
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            if (campaignId is { } cid)
            {
                db.OutreachActivities.RemoveRange(await db.OutreachActivities.Where(a => a.CampaignId == cid).ToListAsync());
                db.OutreachCampaigns.RemoveRange(await db.OutreachCampaigns.Where(x => x.Id == cid).ToListAsync());
            }
            db.OutreachGroupMemberships.RemoveRange(await db.OutreachGroupMemberships.Where(m => createdGroups.Contains(m.GroupId)).ToListAsync());
            db.OutreachShares.RemoveRange(await db.OutreachShares.Where(x => x.FacebookAccountId == pageA.Id || x.FacebookAccountId == pageB.Id).ToListAsync());
            db.OutreachWallPosts.RemoveRange(await db.OutreachWallPosts.Where(x => x.FacebookAccountId == pageA.Id || x.FacebookAccountId == pageB.Id).ToListAsync());
            db.OutreachGroups.RemoveRange(await db.OutreachGroups.Where(g => createdGroups.Contains(g.Id)).ToListAsync());
            db.OutreachContents.RemoveRange(await db.OutreachContents.Where(x => x.Text.Contains(tag)).ToListAsync());
            db.LandingEvents.RemoveRange(await db.LandingEvents.Where(e => e.VisitorId == "v-" + tag).ToListAsync());
            db.FacebookAccounts.RemoveRange(await db.FacebookAccounts.Where(a => a.Id == pageA.Id || a.Id == pageB.Id).ToListAsync());
            await db.SaveChangesAsync();
            await ResetSecurityAsync();
        }
    }
}
