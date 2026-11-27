using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.AiStudio;
using NewsStudio.Infrastructure.Growth;
using NewsStudio.Infrastructure.Studio;
using Xunit;

namespace NewsStudio.Tests;

public class GrowthAutopilotLogicTests
{
    [Fact]
    public void AShortVideoIsUsedWholeOnce()
    {
        GrowthMedia.PickWindow(new List<(double, double)>(), 42, 45, new List<(double, double)>()).Should().Be(((double)0, (double)42));
        GrowthMedia.PickWindow(new List<(double, double)>(), 42, 45, new List<(double, double)> { (0, 42) }).Should().BeNull();
    }

    [Fact]
    public void TheLoudestWindowIsChosenAndUsedPartsAreSkipped()
    {
        // 120 s video, quiet except a loud stretch 60–100 s
        var loud = Enumerable.Range(0, 1200).Select(i => (T: i / 10.0, M: i / 10.0 is >= 60 and < 100 ? -12.0 : -35.0)).ToList();
        var first = GrowthMedia.PickWindow(loud, 120, 45, new List<(double, double)>());
        first.Should().NotBeNull();
        first!.Value.Start.Should().BeInRange(55, 60);
        first.Value.Seconds.Should().Be(45);
        var second = GrowthMedia.PickWindow(loud, 120, 45, new List<(double, double)> { first.Value });
        second.Should().NotBeNull();
        (second!.Value.Start + 45 <= first.Value.Start || second.Value.Start >= first.Value.Start + 45).Should().BeTrue("a used part is never cut again");
        // no sound: windows in order from the start
        GrowthMedia.PickWindow(new List<(double, double)>(), 120, 45, new List<(double, double)>())!.Value.Start.Should().Be(1);
    }

    [Fact]
    public void LoudnessIsReadFromFfmpegOutput()
    {
        var stderr = "[Parsed_ebur128_0 @ 0x1] t: 0.1      TARGET:-23 LUFS    M: -inf S: -inf     I: -70.0 LUFS\n" +
                     "[Parsed_ebur128_0 @ 0x1] t: 0.2      TARGET:-23 LUFS    M: -18.5 S: -20.1   I: -19.0 LUFS\n";
        var l = GrowthMedia.ParseLoudness(stderr);
        l.Should().HaveCount(2);
        l[0].M.Should().Be(-70);
        l[1].Should().Be((0.2, -18.5));
    }

    [Fact]
    public void PassedSlotsGoOutSoonAndLateOnesWaitForTomorrow()
    {
        var day = new DateOnly(2026, 12, 1);
        var morning = GrowthTime.SlotUtc(day, "08:00");
        GrowthAutopilotService.When(day, "11:30", 0, morning, day.AddDays(6)).Should().Be(GrowthTime.SlotUtc(day, "11:30"));
        GrowthAutopilotService.When(day, "07:30", 0, morning, day.AddDays(6)).Should().Be(morning.AddMinutes(15));
        GrowthAutopilotService.When(day, "11:30", 0, GrowthTime.SlotUtc(day, "23:20"), day.AddDays(6)).Should().BeNull();
        GrowthAutopilotService.When(day, "19:30", 0, morning, day.AddDays(-1)).Should().BeNull(); // campaign already over
    }

    [Fact]
    public void ScriptPromptsStayFaithfulAndLabelTheAiPicture()
    {
        var s = Script("T-1");
        var prompt = GrowthAiService.BuildScriptImagePrompt(s);
        prompt.Should().Contain("bamboo kite").And.Contain("no text");
        var (_, user) = GrowthAiService.BuildScriptPostPrompt("Trạm ký ức", s);
        user.Should().Contain("Chỉ dùng chi tiết có trong kịch bản");
        var post = GrowthAiService.ParseScriptPost("{\"caption\":\"Chiều ấy…\",\"question\":\"Bạn còn nhớ không?\",\"hashtags\":[\"#tuoitho\",\"kyniem\"]}");
        post!.Value.Caption.Should().Contain("Bạn còn nhớ không?").And.EndWith(GrowthAiService.ImageDisclosure);
        post.Value.Hashtags.Should().Be("#tuoitho #kyniem");
        GrowthAiService.ParseScriptPost("oops").Should().BeNull();
    }

    [Fact]
    public async Task TheLoudestPartOfARealVideoIsCutForAStory()
    {
        var ffmpeg = StudioMedia.LocateFfmpeg();
        if (ffmpeg == null) return; // FFmpeg is installed on the PC worker and on Railway; skip where it isn't
        var dir = Path.Combine(Path.GetTempPath(), "growth-media-test-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        try
        {
            var video = Path.Combine(dir, "in.mp4");
            var psi = new System.Diagnostics.ProcessStartInfo(ffmpeg) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "-y", "-f", "lavfi", "-i", "testsrc=size=270x480:rate=10", "-f", "lavfi", "-i", "sine=f=440:d=70",
                         "-af", "volume='if(between(t,40,60),1,0.02)':eval=frame", "-t", "70", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-shortest", video })
                psi.ArgumentList.Add(a);
            using (var p = System.Diagnostics.Process.Start(psi)!) { await p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync(); }
            File.Exists(video).Should().BeTrue();

            var media = new GrowthMedia();
            var loudness = await media.LoudnessAsync(video, CancellationToken.None);
            loudness.Should().NotBeEmpty();
            var w = GrowthMedia.PickWindow(loudness, 70, 45, new List<(double, double)>());
            w.Should().NotBeNull();
            (w!.Value.Start <= 40 && w.Value.Start + 45 >= 60).Should().BeTrue("the window covers the loud part 40–60 s");

            var cut = Path.Combine(dir, "story.mp4");
            await media.CutAsync(video, w.Value.Start, w.Value.Seconds, cut, CancellationToken.None);
            var seconds = await new StudioMedia(new ConfigurationBuilder().Build()).ProbeSecondsAsync(cut, CancellationToken.None);
            seconds.Should().BeInRange(43, 47);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void NewsPagesGetNewsAndNostalgiaPagesGetScripts()
    {
        GrowthNewsService.EffectiveMode("Auto", hasNewsVideos: true).Should().Be("News");
        GrowthNewsService.EffectiveMode("Auto", hasNewsVideos: false).Should().Be("Scripts");
        GrowthNewsService.EffectiveMode(null, hasNewsVideos: true).Should().Be("News");
        GrowthNewsService.EffectiveMode("Scripts", hasNewsVideos: true).Should().Be("Scripts");
        GrowthNewsService.EffectiveMode("News", hasNewsVideos: false).Should().Be("News");
        GrowthAutopilotService.IsNewsSlot("auto:abc:20261127:npost").Should().BeTrue();
        GrowthAutopilotService.IsNewsSlot("auto:abc:20261127:nstory1").Should().BeTrue();
        GrowthAutopilotService.IsNewsSlot("auto:abc:20261127:post").Should().BeFalse();
        GrowthAutopilotService.IsNewsSlot("auto:abc:20261127:story2").Should().BeFalse();
    }

    [Fact]
    public void NewsPostTextNamesItsSourceAndKeepsCardTextShort()
    {
        var t = GrowthNewsService.ParsePost("{\"headline\":\"Việt Nam thắng 2-0\",\"summary\":\"Đội tuyển thắng Lào ở vòng bảng.\",\"caption\":\"Tối qua…\",\"hashtags\":[\"bongda\",\"#doituyen\"]}", "VnExpress");
        t.Should().NotBeNull();
        t!.Caption.Should().EndWith("📰 Nguồn: VnExpress");
        t.Hashtags.Should().Be("#bongda #doituyen");
        GrowthNewsService.ParsePost("{\"headline\":\"\"}", "X").Should().BeNull();
        var (_, user) = GrowthNewsService.BuildPostPrompt("Page", "Thể thao", new TrendingArticleDto { Title = "T", SourceName = "S" }, "nội dung");
        user.Should().Contain("tối đa 12 chữ").And.Contain("không bịa");
    }

    internal static GrowthScript Script(string code, string category = "tro_choi_tho_au") => new()
    {
        Code = code, Title = "Thả diều " + code, Category = category, CategoryName = category, Era = "9x", Region = "Bắc Bộ", Mood = "Ấm áp hoài niệm",
        Synopsis = "Chiều hè, lũ trẻ thả diều trên đê.", Hook = "Con diều giấy bố làm.", EndingNote = "Gió vẫn thổi.",
        ScenesJson = JsonSerializer.Serialize(new[]
        {
            new { title = "1", visualVi = "Đê làng", aiPromptEn = "kids on a dike at sunset", voiceover = "Chiều ấy" },
            new { title = "2", visualVi = "Con diều", aiPromptEn = "a handmade bamboo kite rising over rice fields", voiceover = "Diều lên" },
            new { title = "3", visualVi = "Về nhà", aiPromptEn = "walking home", voiceover = "Tối về" }
        })
    };
}

[Collection("DatabaseCollection")]
public class GrowthAutopilotApiTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public GrowthAutopilotApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // pages made by a test are removed afterwards (with the existing "delete page"), so the shared test DB doesn't grow
    private readonly List<Guid> _createdPages = new();
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var fb = scope.ServiceProvider.GetRequiredService<NewsStudio.Application.Interfaces.IFacebookPublishingService>();
        foreach (var id in _createdPages)
        {
            try
            {
                await db.GrowthScheduledContents.Where(x => db.GrowthCampaigns.Any(c => c.Id == x.CampaignId && c.FacebookAccountId == id))
                    .ExecuteUpdateAsync(x => x.SetProperty(c => c.Status, "Cancelled"));
                await fb.DeletePageAsync(id);
            }
            catch { /* best effort */ }
        }
    }

    private sealed class FakeText : IGrowthTextAi
    {
        public int Calls;
        public Task<(string Json, decimal CostUsd, long Tokens, string Model)> JsonAsync(string system, string user, int maxTokens, double temperature, CancellationToken ct)
        {
            Calls++;
            var json = system.Contains("hoài niệm tuổi thơ")
                ? "{\"caption\":\"Chiều ấy trên đê làng…\",\"question\":\"Bạn còn nhớ con diều đầu tiên?\",\"hashtags\":[\"#tuoitho\"]}"
                : "{\"variants\":[{\"style\":\"Nostalgic\",\"caption\":\"Video ký ức\",\"hashtags\":[\"#kyuc\"]}]}";
            return Task.FromResult((json, 0.001m, 100L, "fake-text"));
        }
    }

    private sealed class FakeImage : IGrowthImageAi
    {
        // 1×1 PNG
        private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        public int Calls;
        public decimal EstimatedCostUsd => 0.05m;
        public Task<(byte[] Bytes, string MimeType, decimal CostUsd, string Model)> ImageAsync(string prompt, string aspectRatio, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult((Png, "image/png", 0.04m, "fake-image"));
        }
    }

    private (GrowthAutopilotService Service, IApplicationDbContext Db, FakeText Text, FakeImage Image) Build(IServiceScope scope)
    {
        var sp = scope.ServiceProvider;
        var text = new FakeText();
        var image = new FakeImage();
        var ai = new GrowthAiService(sp.GetRequiredService<IApplicationDbContext>(), text, image, sp.GetRequiredService<StudioService>(),
            sp.GetRequiredService<GrowthCampaignService>(), sp.GetRequiredService<GrowthInsightsService>());
        var service = new GrowthAutopilotService(sp.GetRequiredService<IApplicationDbContext>(), sp.GetRequiredService<GrowthCampaignService>(),
            sp.GetRequiredService<GrowthContentService>(), sp.GetRequiredService<GrowthInsightsService>(), ai, sp.GetRequiredService<GrowthMedia>(),
            sp.GetRequiredService<AiStudioMedia>(), sp.GetRequiredService<StudioService>(), sp.GetRequiredService<StudioMedia>(),
            sp.GetRequiredService<AiStudioService>(), sp.GetRequiredService<GrowthNewsService>(), NullLogger<GrowthAutopilotService>.Instance)
        {
            // 10:00 Vietnam time today: the 11:30 post slot is still ahead
            Clock = () => GrowthTime.DayStartUtc(GrowthTime.Today(DateTime.UtcNow)).AddHours(10)
        };
        return (service, sp.GetRequiredService<IApplicationDbContext>(), text, image);
    }

    private async Task<FacebookAccount> NewPageAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var page = new FacebookAccount { PageId = "8" + Random.Shared.NextInt64(100000000, 999999999), PageName = "Trạm thử " + Guid.NewGuid().ToString("N")[..5], EncryptedAccessToken = "test-token" };
        db.FacebookAccounts.Add(page);
        _createdPages.Add(page.Id);
        db.GrowthAutopilots.Add(new GrowthAutopilot { FacebookAccountId = page.Id, Enabled = true, ScriptPost = true, DailyVideo = true, StoriesPerDay = 2 });
        await db.SaveChangesAsync();
        return page;
    }

    /// <summary>Script codes of this test run only (the pool is shared by all Pages).</summary>
    private async Task<List<string>> ImportAsync(int count)
    {
        var prefix = "TST-" + Guid.NewGuid().ToString("N")[..6];
        using var scope = _factory.Services.CreateScope();
        var (service, _, _, _) = Build(scope);
        var items = Enumerable.Range(1, count).Select(i =>
        {
            var s = GrowthAutopilotLogicTests.Script($"{prefix}-{i:000}", i % 2 == 0 ? "bua_com_ngheo" : "tro_choi_tho_au");
            return new GrowthScriptImportDto
            {
                Code = s.Code, Title = s.Title, Category = s.Category, CategoryName = s.CategoryName, Era = s.Era, Region = s.Region, Mood = s.Mood,
                Synopsis = s.Synopsis, Hook = s.Hook, EndingNote = s.EndingNote,
                Scenes = new() { new GrowthScriptSceneDto { Title = "1", VisualVi = "Đê", AiPromptEn = "kids flying a kite", Voiceover = "Chiều" } }
            };
        }).ToList();
        (await service.ImportScriptsAsync(items, CancellationToken.None)).Should().Be(count);
        (await service.ImportScriptsAsync(items, CancellationToken.None)).Should().Be(0, "importing twice adds nothing");
        return items.Select(i => i.Code).ToList();
    }

    [Fact]
    public async Task TheDailyPostUsesAnUnusedScriptOnceAndNeverTwice()
    {
        var page = await NewPageAsync();
        await ImportAsync(3);
        GrowthAutopilotDto status;
        using (var scope = _factory.Services.CreateScope())
        {
            var (service, db, text, image) = Build(scope);
            status = await service.RunAsync(page.Id, force: false, CancellationToken.None);
            image.Calls.Should().Be(1);
            var post = status.Today.Single(x => x.Kind == "Photo");
            post.Status.Should().Be("Scheduled");
            post.Caption.Should().Contain("Bạn còn nhớ con diều đầu tiên?").And.Contain(GrowthAiService.ImageDisclosure);
            post.Assets.Should().ContainSingle();
            var campaign = await db.GrowthCampaigns.AsNoTracking().FirstAsync(c => c.Id == status.CampaignId);
            campaign.Status.Should().Be("Running");
            campaign.ApprovalMode.Should().Be("Auto");
            campaign.AiSpentUsd.Should().BeGreaterThan(0);
            // the 2nd Story is the day's picture
            status.Today.Should().Contain(x => x.Kind == "Story" && x.Assets.Count == 1 && x.Assets[0].Id == post.Assets[0].Id);
            // a Reel only from a real finished video; without one the owner is told (other tests may leave unassigned videos in the shared test DB)
            (status.Today.Any(x => x.Kind == "Reel" && x.SourceId != null) || status.Notices.Any(n => n.Code == "no_video")).Should().BeTrue();
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var (service, db, _, image) = Build(scope);
            var again = await service.RunAsync(page.Id, force: false, CancellationToken.None);
            image.Calls.Should().Be(0, "today's post already exists");
            again.Today.Count(x => x.Kind == "Photo").Should().Be(1);
            (await db.GrowthScriptUsages.CountAsync(u => u.FacebookAccountId == page.Id)).Should().Be(1);
        }
    }

    [Fact]
    public async Task ScriptsRotateTopicsAndRunningOutIsReported()
    {
        var page = await NewPageAsync();
        using var scope = _factory.Services.CreateScope();
        var (service, db, _, _) = Build(scope);
        // mark every script in the pool as used, except two of a fresh import
        var codes = await ImportAsync(2);
        var keep = await db.GrowthScripts.AsNoTracking().Where(s => codes.Contains(s.Code)).Select(s => s.Id).ToListAsync();
        var others = await db.GrowthScripts.AsNoTracking().Where(s => !keep.Contains(s.Id)).Select(s => s.Id).ToListAsync();
        foreach (var id in others) db.GrowthScriptUsages.Add(new GrowthScriptUsage { FacebookAccountId = page.Id, ScriptId = id });
        await db.SaveChangesAsync();

        var first = await service.NextScriptAsync(page.Id, CancellationToken.None);
        first.Should().NotBeNull();
        db.GrowthScriptUsages.Add(new GrowthScriptUsage { FacebookAccountId = page.Id, ScriptId = first!.Id });
        await db.SaveChangesAsync();
        var second = await service.NextScriptAsync(page.Id, CancellationToken.None);
        second!.Category.Should().NotBe(first.Category, "topics take turns");
        db.GrowthScriptUsages.Add(new GrowthScriptUsage { FacebookAccountId = page.Id, ScriptId = second.Id });
        await db.SaveChangesAsync();
        (await service.NextScriptAsync(page.Id, CancellationToken.None)).Should().BeNull();

        // using a script twice on the same Page is impossible
        db.GrowthScriptUsages.Add(new GrowthScriptUsage { FacebookAccountId = page.Id, ScriptId = second.Id });
        var dup = async () => await db.SaveChangesAsync();
        await dup.Should().ThrowAsync<DbUpdateException>();
        if (db is DbContext ctx) ctx.ChangeTracker.Clear();

        var status = await service.RunAsync(page.Id, force: false, CancellationToken.None);
        status.Scripts.Remaining.Should().Be(0);
        status.Notices.Should().Contain(n => n.Code == "scripts_empty" && n.Level == "error");
        status.Today.Should().NotContain(x => x.Kind == "Photo");
    }

    [Fact]
    public async Task TheNewestFinishedVideoBecomesTheDailyReel()
    {
        var page = await NewPageAsync();
        await ImportAsync(1);
        Guid videoId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var v = new StudioVideo { Title = "Ký ức bếp lửa", FacebookAccountId = page.Id, Status = "Ready", FinalBlobId = Guid.NewGuid(), FinalDurationSeconds = 70, Caption = "Bếp lửa của mẹ 🔥" };
            db.StudioVideos.Add(v);
            await db.SaveChangesAsync();
            videoId = v.Id;
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var (service, _, _, _) = Build(scope);
            var status = await service.RunAsync(page.Id, force: false, CancellationToken.None);
            var reel = status.Today.Single(x => x.Kind == "Reel");
            reel.SourceId.Should().Be(videoId);
            reel.Caption.Should().Be("Bếp lửa của mẹ 🔥");
            status.Notices.Should().NotContain(n => n.Code == "no_video");
        }
    }

    [Fact]
    public async Task ACancelledPostGivesItsScriptBack()
    {
        var page = await NewPageAsync();
        await ImportAsync(1);
        using var scope = _factory.Services.CreateScope();
        var (service, db, _, _) = Build(scope);
        var status = await service.RunAsync(page.Id, force: false, CancellationToken.None);
        var post = status.Today.Single(x => x.Kind == "Photo");
        await db.GrowthScheduledContents.Where(x => x.Id == post.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Cancelled"));
        await service.RunAsync(page.Id, force: false, CancellationToken.None);
        (await db.GrowthScriptUsages.AnyAsync(u => u.ContentId == post.Id)).Should().BeFalse();
    }

    /// <summary>A news Page: "Tự động đăng" on, with a topic no story matches (so nothing is fetched from the internet).</summary>
    private async Task<FacebookAccount> NewNewsPageAsync()
    {
        var page = await NewPageAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        db.AutoPostSettings.Add(new AutoPostSettings { FacebookAccountId = page.Id, Enabled = true, Topic = "tu-chon", TopicKeywords = "zzqxw-khong-co-tin-nao" });
        await db.SaveChangesAsync();
        return page;
    }

    [Fact]
    public async Task ANewsPageNeverGetsScriptPosts()
    {
        var page = await NewNewsPageAsync();
        await ImportAsync(2);
        using var scope = _factory.Services.CreateScope();
        var (service, db, _, image) = Build(scope);
        var status = await service.RunAsync(page.Id, force: false, CancellationToken.None);
        status.EffectiveMode.Should().Be("News");
        image.Calls.Should().Be(0, "no AI picture of a drama script for a news Page");
        status.Today.Should().NotContain(x => x.ScriptId != null);
        (await db.GrowthScriptUsages.AnyAsync(u => u.FacebookAccountId == page.Id)).Should().BeFalse();
        status.Notices.Should().Contain(n => n.Message.Contains("không còn tin hot"), "no story of its topic → told, nothing off-topic posted");
    }

    [Fact]
    public async Task ScriptItemsAlreadyScheduledOnANewsPageAreCancelledAndNeverPosted()
    {
        var page = await NewPageAsync();
        await ImportAsync(1);
        Guid postId;
        using (var scope = _factory.Services.CreateScope())
        {
            // made while the Page was (wrongly) in script mode
            var (service, _, _, _) = Build(scope);
            var status = await service.RunAsync(page.Id, force: false, CancellationToken.None);
            postId = status.Today.Single(x => x.Kind == "Photo").Id;
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.AutoPostSettings.Add(new AutoPostSettings { FacebookAccountId = page.Id, Enabled = true, Topic = "tu-chon", TopicKeywords = "zzqxw-khong-co-tin-nao" });
            await db.SaveChangesAsync();
            // even before the autopilot runs again, the publisher refuses it
            await db.GrowthScheduledContents.Where(x => x.Id == postId).ExecuteUpdateAsync(x => x.SetProperty(c => c.ScheduledAtUtc, DateTime.UtcNow.AddMinutes(-1)));
            var gateway = new CountingGateway();
            var publisher = new GrowthPublisher(db, gateway, scope.ServiceProvider.GetRequiredService<GrowthCampaignService>(),
                scope.ServiceProvider.GetRequiredService<Hangfire.IBackgroundJobClient>(), NullLogger<GrowthPublisher>.Instance);
            await publisher.PublishOneAsync(postId, CancellationToken.None);
            gateway.Calls.Should().Be(0);
            var item = await db.GrowthScheduledContents.AsNoTracking().FirstAsync(x => x.Id == postId);
            item.Status.Should().Be("Cancelled");
            item.LastError.Should().Contain("Không đúng chủ đề");
        }
    }

    private sealed class CountingGateway : IGrowthPublishGateway
    {
        public int Calls;
        public Task<GrowthPublishResult> PublishAsync(GrowthCampaign campaign, GrowthScheduledContent item, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new GrowthPublishResult("1_1", null, null, null));
        }
    }

    [Fact]
    public async Task AutopilotEndpointsNeedTheKey()
    {
        var config = _factory.Services.GetRequiredService<IConfiguration>();
        var before = config["Ai:Gemini:ApiKey"];
        config["Ai:Gemini:ApiKey"] = "autopilot-key";
        try
        {
            (await _client.GetAsync($"/api/growth/autopilot?pageId={Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await _client.PostAsync("/api/growth/scripts/import", new StringContent("[]", System.Text.Encoding.UTF8, "application/json"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/growth/autopilot?pageId={Guid.NewGuid()}");
            req.Headers.Add("X-Studio-Key", "autopilot-key");
            (await _client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally { config["Ai:Gemini:ApiKey"] = before; }
    }
}
