using System.Text.Json;
using Microsoft.Extensions.Configuration;
using FluentAssertions;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Studio;
using Xunit;

namespace NewsStudio.Tests;

/// <summary>AI video studio: Grok Imagine request/response handling, shot planning, prompts and FFmpeg work.</summary>
public class StudioTests
{
    [Fact]
    public void Grok_request_sends_start_frame_as_data_uri_and_clamps_duration()
    {
        var body = GrokVideoClient.BuildRequest("grok-imagine-video-1.5-lite", "A man calls out",
            new StartFrame(new byte[] { 1, 2, 3 }, "image/jpeg"), "9:16", "720p", 30);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(body));
        var r = doc.RootElement;

        r.GetProperty("model").GetString().Should().Be("grok-imagine-video-1.5-lite");
        r.GetProperty("prompt").GetString().Should().Be("A man calls out");
        r.GetProperty("duration").GetInt32().Should().Be(15); // model max
        r.GetProperty("aspect_ratio").GetString().Should().Be("9:16");
        r.GetProperty("resolution").GetString().Should().Be("720p");
        r.GetProperty("generate_audio").GetBoolean().Should().BeTrue();
        r.GetProperty("image").GetProperty("url").GetString().Should().Be("data:image/jpeg;base64,AQID");
    }

    [Fact]
    public void Grok_request_without_frame_is_text_to_video()
    {
        var body = GrokVideoClient.BuildRequest("m", "x", null, "16:9", "4k", 0);
        body.Should().NotContainKey("image");
        body["resolution"].Should().Be("720p"); // unknown resolution → default
        body["duration"].Should().Be(1);
        body["aspect_ratio"].Should().Be("16:9");
    }

    [Theory]
    [InlineData("""{"status":"pending"}""", "pending", null, null, null)]
    [InlineData("""{"status":"done","video":{"url":"https://vidgen.x.ai/a/video.mp4","duration":8,"respect_moderation":true},"model":"grok-imagine-video-1.5-lite"}""", "done", "https://vidgen.x.ai/a/video.mp4", true, null)]
    [InlineData("""{"status":"done","video":{"url":"https://vidgen.x.ai/b.mp4","respect_moderation":false}}""", "done", "https://vidgen.x.ai/b.mp4", false, null)]
    [InlineData("""{"status":"failed","error":{"code":"invalid_argument","message":"Prompt cannot be empty."}}""", "failed", null, null, "invalid_argument")]
    [InlineData("""{"video":{"url":"https://vidgen.x.ai/c.mp4"}}""", "done", "https://vidgen.x.ai/c.mp4", null, null)]
    public void Grok_status_is_read(string json, string status, string? url, bool? respect, string? code)
    {
        var s = GrokVideoClient.ParseStatus(json);
        s.Status.Should().Be(status);
        s.VideoUrl.Should().Be(url);
        s.RespectModeration.Should().Be(respect);
        s.ErrorCode.Should().Be(code);
    }

    [Fact]
    public void Cost_is_seconds_times_price_plus_the_image_fee()
    {
        var client = Client(new FakeXai());
        client.EstimateCost(10, "720p", false).Should().Be(0.30m);
        client.EstimateCost(10, "720p", true).Should().Be(0.31m);
        client.EstimateCost(10, "480p", false).Should().Be(0.20m);
        client.EstimateCost(10, "1080p", false).Should().Be(1.40m);
    }

    [Fact]
    public async Task Grok_job_is_started_polled_and_downloaded()
    {
        var xai = new FakeXai();
        xai.Polls.Enqueue("""{"status":"pending"}""");
        xai.Polls.Enqueue("""{"status":"done","video":{"url":"https://vidgen.x.ai/r1/video.mp4","duration":8,"respect_moderation":true}}""");
        var client = Client(xai);

        var clip = await client.GenerateAsync("prompt", new StartFrame(new byte[] { 9 }, "image/jpeg"), "9:16", "720p", 8, CancellationToken.None);

        clip.Video.Should().Equal(FakeXai.VideoBytes);
        clip.RequestId.Should().Be("r1");
        clip.CostUsd.Should().Be(0.25m); // 8 × 0.03 + 0.01 image
        xai.StartBody.Should().Contain("\"model\":\"grok-imagine-video-1.5-lite\"").And.Contain("data:image/jpeg;base64,CQ==");
        xai.AuthHeaders.Should().OnlyContain(h => h == "Bearer test-xai-key");
    }

    [Fact]
    public async Task Grok_resumes_a_paid_job_instead_of_paying_again()
    {
        var xai = new FakeXai();
        xai.Old["old1"] = """{"status":"done","video":{"url":"https://vidgen.x.ai/old1/video.mp4","respect_moderation":true}}""";
        string? started = null;
        var clip = await Client(xai).GenerateAsync("p", null, "9:16", "720p", 8, CancellationToken.None, "old1", id => { started = id; return Task.CompletedTask; });

        xai.Starts.Should().Be(0);          // nothing new submitted
        started.Should().BeNull();
        clip.RequestId.Should().Be("old1");
        clip.CostUsd.Should().Be(0m);       // already paid in the earlier run
        clip.Video.Should().Equal(FakeXai.VideoBytes);
    }

    [Theory]
    [InlineData(null)]                                                                          // xAI no longer knows it (404)
    [InlineData("""{"status":"failed","error":{"code":"invalid_argument","message":"x"}}""")]  // it had failed
    [InlineData("""{"status":"done","video":{"url":"https://vidgen.x.ai/f.mp4","respect_moderation":false}}""")] // filtered
    public async Task Grok_starts_afresh_when_the_old_job_is_not_reusable(string? oldAnswer)
    {
        var xai = new FakeXai();
        if (oldAnswer != null) xai.Old["old1"] = oldAnswer;
        xai.Polls.Enqueue("""{"status":"done","video":{"url":"https://vidgen.x.ai/r1/video.mp4","respect_moderation":true}}""");
        string? started = null;
        var clip = await Client(xai).GenerateAsync("p", null, "9:16", "720p", 8, CancellationToken.None, "old1", id => { started = id; return Task.CompletedTask; });

        xai.Starts.Should().Be(1);
        started.Should().Be("r1");          // the new id is handed over as soon as the job exists
        clip.RequestId.Should().Be("r1");
        clip.CostUsd.Should().Be(0.24m);
    }

    [Theory]
    [InlineData(402, "Payment required")]
    [InlineData(403, "forbidden")]
    [InlineData(429, "You have run out of credits")] // a spent balance must stop the run, not be retried as "busy"
    public async Task Grok_unpaid_account_stops_the_run(int status, string message)
    {
        var xai = new FakeXai { StartStatus = (System.Net.HttpStatusCode)status, StartError = message };
        var act = () => Client(xai).GenerateAsync("p", null, "9:16", "720p", 5, CancellationToken.None);
        (await act.Should().ThrowAsync<VideoAccountException>()).Which.Message.Should().Contain("Tạo các clip còn thiếu");
    }

    [Fact]
    public async Task Grok_recover_downloads_a_paid_clip_again_without_a_new_job()
    {
        var xai = new FakeXai();
        xai.Old["paid1"] = """{"status":"done","video":{"url":"https://vidgen.x.ai/paid1/video.mp4","respect_moderation":true}}""";
        xai.Old["gone1"] = """{"status":"expired"}""";

        var got = await Client(xai).TryRecoverAsync("paid1", CancellationToken.None);
        var expired = await Client(xai).TryRecoverAsync("gone1", CancellationToken.None);
        var unknown = await Client(xai).TryRecoverAsync("never", CancellationToken.None);

        got!.Value.Bytes.Should().Equal(FakeXai.VideoBytes);
        expired.Should().BeNull();
        unknown.Should().BeNull();
        xai.Starts.Should().Be(0); // recovering never pays for a new clip
    }

    [Fact]
    public async Task Grok_busy_is_transient()
    {
        var xai = new FakeXai { StartStatus = (System.Net.HttpStatusCode)429 };
        var act = () => Client(xai).GenerateAsync("p", null, "9:16", "720p", 5, CancellationToken.None);
        await act.Should().ThrowAsync<VideoTransientException>();
    }

    [Fact]
    public async Task Grok_moderation_failure_is_a_rejection_not_a_retry()
    {
        var xai = new FakeXai();
        xai.Polls.Enqueue("""{"status":"failed","error":{"code":"invalid_argument","message":"content blocked by moderation"}}""");
        var act = () => Client(xai).GenerateAsync("p", null, "9:16", "720p", 5, CancellationToken.None);
        (await act.Should().ThrowAsync<VideoRejectedException>()).Which.Message.Should().Contain("moderation");
    }

    [Fact]
    public async Task Grok_filtered_clip_is_rejected_and_still_billed()
    {
        var xai = new FakeXai();
        xai.Polls.Enqueue("""{"status":"done","video":{"url":"https://vidgen.x.ai/r1/video.mp4","respect_moderation":false}}""");
        var act = () => Client(xai).GenerateAsync("p", null, "9:16", "720p", 5, CancellationToken.None);
        (await act.Should().ThrowAsync<VideoRejectedException>()).Which.BilledUsd.Should().Be(0.15m);
    }

    private static GrokVideoClient Client(FakeXai xai)
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:Xai:ApiKey"] = "test-xai-key" }).Build();
        return new GrokVideoClient(new HttpClient(xai), config, Microsoft.Extensions.Logging.Abstractions.NullLogger<GrokVideoClient>.Instance)
        {
            PollInterval = TimeSpan.FromMilliseconds(1)
        };
    }

    /// <summary>Stands in for api.x.ai: start → request_id, polls answered from a queue, video download.</summary>
    private sealed class FakeXai : HttpMessageHandler
    {
        public static readonly byte[] VideoBytes = Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray();
        public Queue<string> Polls { get; } = new();
        /// <summary>Jobs from an earlier run (resume): id → poll answer; unknown ids answer 404.</summary>
        public Dictionary<string, string> Old { get; } = new();
        public int Starts { get; private set; }
        public System.Net.HttpStatusCode StartStatus { get; set; } = System.Net.HttpStatusCode.OK;
        public string StartError { get; set; } = "busy";
        public string StartBody { get; private set; } = "";
        public List<string> AuthHeaders { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith("https://vidgen.x.ai/"))
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(VideoBytes) { Headers = { ContentType = new("video/mp4") } }
                };
            AuthHeaders.Add(request.Headers.Authorization?.ToString() ?? "");
            if (request.Method == HttpMethod.Post && url == "https://api.x.ai/v1/videos/generations")
            {
                Starts++;
                StartBody = await request.Content!.ReadAsStringAsync(ct);
                if (StartStatus != System.Net.HttpStatusCode.OK)
                    return new HttpResponseMessage(StartStatus) { Content = new StringContent(JsonSerializer.Serialize(new { error = StartError })) };
                return Json("""{"request_id":"r1"}""");
            }
            if (request.Method == HttpMethod.Get && url == "https://api.x.ai/v1/videos/r1")
                return Json(Polls.Count > 0 ? Polls.Dequeue() : """{"status":"pending"}""");
            if (request.Method == HttpMethod.Get && url.StartsWith("https://api.x.ai/v1/videos/") && Old.TryGetValue(url[27..], out var old))
                return Json(old);
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) =>
            new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }

    [Fact]
    public void Plan_reads_continue_flag_but_never_on_the_first_shot()
    {
        var plan = StudioService.ParsePlan("""
            {"shots":[{"duration":6,"action":"a","continueFromPrevious":true},
                      {"duration":6,"action":"b","continueFromPrevious":true},
                      {"duration":6,"action":"c","continueFromPrevious":false},
                      {"duration":6,"action":"d"}]}
            """, new HashSet<Guid>(), new HashSet<Guid>());
        plan.Shots.Select(s => s.ContinueFromPrevious).Should().Equal(false, true, false, false);
    }

    [Fact]
    public void Continued_shot_prompt_asks_for_a_seamless_take()
    {
        var prompt = StudioService.BuildShotPrompt(new StudioVideo(), new StudioShot { Action = "He laughs" }, new Dictionary<Guid, StudioAsset>(), continued: true);
        prompt.Should().Contain("Continue seamlessly from the starting image");
    }

    [Fact]
    public void Plan_keeps_only_known_ids_and_valid_durations()
    {
        var hai = Guid.NewGuid();
        var village = Guid.NewGuid();
        var song = Guid.NewGuid();
        var json = $$"""
            Đây là kế hoạch: {"caption":"Cá lóc nướng!","notes":"vui","musicTrackId":"{{song}}",
             "shots":[{"duration":9.4,"assetIds":["{{hai}}","{{village}}","{{Guid.NewGuid()}}"],"action":"Anh Hai fans the charcoal","camera":"medium shot","speaker":"Anh Hai","dialogue":"Ê Út Tèo!","sound":"crackling charcoal"},
                      {"duration":40,"assetIds":[],"action":"Wide shot of the lane","camera":"static","speaker":"null","dialogue":null,"sound":""},
                      {"duration":1,"assetIds":[],"action":"","camera":"","dialogue":null}]}
            """;
        var plan = StudioService.ParsePlan(json, new HashSet<Guid> { hai, village }, new HashSet<Guid> { song });

        plan.Caption.Should().Be("Cá lóc nướng!");
        plan.MusicTrackId.Should().Be(song);
        plan.Shots.Should().HaveCount(2); // the empty third shot is dropped
        plan.Shots[0].Duration.Should().Be(9);
        plan.Shots[0].AssetIds.Should().Equal(hai, village); // unknown id removed
        plan.Shots[0].Dialogue.Should().Be("Ê Út Tèo!");
        plan.Shots[1].Duration.Should().Be(15); // clamped to the model's max
        plan.Shots[1].Speaker.Should().BeNull(); // "null" string treated as none
    }

    [Fact]
    public void Plan_prompt_follows_the_script_without_a_target_length()
    {
        var (_, user) = StudioService.BuildPlanPrompt(new StudioVideo { Script = "Anh Hai: Ê Út Tèo!", TargetDurationSeconds = 180 },
            new List<StudioAsset>(), new List<StudioMusicTrack>());
        user.Should().Contain("Độ dài video do KỊCH BẢN quyết định").And.Contain("KHÔNG thêm shot độn");
        user.Should().NotContain("180").And.NotContain("±10%"); // the typed number never reaches the planner
    }

    [Fact]
    public void Plan_gives_every_line_enough_time_to_be_said()
    {
        var plan = StudioService.ParsePlan("""
            {"shots":[{"duration":5,"action":"a","speaker":"Công Tôn Sách","dialogue":"Còn nghiêm trọng hơn ạ! Phu nhân bảo nếu đại nhân không về ngay thì tối nay ngủ ngoài cửa!"},
                      {"duration":3,"action":"b","speaker":"Bao Công","dialogue":"Án mạng à?"},
                      {"duration":6,"action":"c"}]}
            """, new HashSet<Guid>(), new HashSet<Guid>());
        plan.Shots[0].Duration.Should().Be(8); // 20 words / 3 + 1 → 8 s instead of a rushed 5 s
        plan.Shots[1].Duration.Should().Be(3); // short line keeps the planner's time
        plan.Shots[2].Duration.Should().Be(6); // no dialogue: untouched
        StudioService.SecondsToSay(string.Join(' ', Enumerable.Repeat("chữ", 60))).Should().Be(21); // >15 → clamped by the clip limit
    }

    [Fact]
    public void Plan_with_unknown_music_falls_back_to_none()
    {
        var plan = StudioService.ParsePlan("""{"musicTrackId":"not-a-guid","shots":[{"duration":8,"action":"x"}]}""", new HashSet<Guid>(), new HashSet<Guid>());
        plan.MusicTrackId.Should().BeNull();
    }

    [Fact]
    public void Shot_prompt_names_characters_location_and_exact_vietnamese_line()
    {
        var hai = new StudioAsset { Id = Guid.NewGuid(), Kind = "Character", Name = "Anh Hai", Description = "45-year-old man, thin moustache, blue shirt" };
        var porch = new StudioAsset { Id = Guid.NewGuid(), Kind = "Location", Name = "Hàng ba nhà quê", Description = "rustic Mekong Delta porch" };
        var video = new StudioVideo { AspectRatio = "9:16", StyleNotes = "giọng miền Tây, nắng chiều" };
        var shot = new StudioShot
        {
            Action = "He fans the charcoal and waves", Camera = "medium shot, static", Speaker = "Anh Hai", Dialogue = "Ê Út Tèo! Tấp vô đây!",
            Sound = "crackling charcoal", AssetIdsJson = JsonSerializer.Serialize(new[] { hai.Id, porch.Id })
        };
        var prompt = StudioService.BuildShotPrompt(video, shot, new Dictionary<Guid, StudioAsset> { [hai.Id] = hai, [porch.Id] = porch });

        prompt.Should().Contain("vertical 9:16").And.Contain("Hàng ba nhà quê").And.Contain("Anh Hai: 45-year-old man");
        prompt.Should().Contain("says exactly: \"Ê Út Tèo! Tấp vô đây!\"").And.Contain("Only Anh Hai speaks");
        prompt.Should().Contain("No background music").And.Contain("No subtitles");
        prompt.Should().Contain("giọng miền Tây");
    }

    [Fact]
    public void Shot_without_dialogue_says_so()
    {
        var prompt = StudioService.BuildShotPrompt(new StudioVideo(), new StudioShot { Action = "Smoke rises from the fish" }, new Dictionary<Guid, StudioAsset>());
        prompt.Should().Contain("No dialogue");
    }

    [Theory]
    [InlineData("Cá lóc nướng thơm lừng!", "Cá lóc nướng thơm lừng!\n\n🤖 Video được tạo bằng AI.")]
    [InlineData("Phim ngắn (tạo bằng AI)", "Phim ngắn (tạo bằng AI)")]
    [InlineData(null, "🤖 Video được tạo bằng AI.")]
    public void Caption_always_discloses_ai(string? caption, string expected) =>
        StudioService.CaptionWithDisclosure(caption).Should().Be(expected);

    [Fact]
    public async Task Compose_joins_clips_even_without_audio_and_mixes_music()
    {
        var ffmpeg = StudioMedia.LocateFfmpeg();
        if (ffmpeg == null) return; // no FFmpeg on this machine: nothing to exercise

        var dir = StudioMedia.TempDir();
        try
        {
            async Task Make(string args)
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ffmpeg, "-hide_banner -loglevel error -y " + args)
                    { UseShellExecute = false, RedirectStandardError = true })!;
                await p.WaitForExitAsync();
                p.ExitCode.Should().Be(0, await p.StandardError.ReadToEndAsync());
            }
            var a = Path.Combine(dir, "a.mp4");   // generated clip with speech
            var b = Path.Combine(dir, "b.mp4");   // clip without any audio stream
            var music = Path.Combine(dir, "m.mp3");
            await Make($"-f lavfi -i testsrc=size=720x1280:rate=24 -f lavfi -i sine=f=300 -t 3 -c:v libx264 -pix_fmt yuv420p -c:a aac \"{a}\"");
            await Make($"-f lavfi -i testsrc=size=1280x720:rate=24 -t 2 -c:v libx264 -pix_fmt yuv420p \"{b}\"");
            await Make($"-f lavfi -i sine=f=600 -t 1.5 -c:a libmp3lame \"{music}\""); // shorter than the video: must loop

            var media = new StudioMedia(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            var output = Path.Combine(dir, "final.mp4");
            var seconds = await media.ComposeAsync(new[] { a, b }, music, 0.15, "9:16", "720p", output, CancellationToken.None);

            seconds.Should().BeApproximately(5.0, 0.3);
            File.Exists(output).Should().BeTrue();
            (await media.ProbeFrameRateAsync(output, CancellationToken.None)).Should().Be("24/1"); // clips' own rate kept, no 24→30 judder
        }
        finally
        {
            StudioMedia.TryDelete(dir);
        }
    }

    [Fact]
    public async Task Compose_burns_in_the_page_watermark_and_tags_the_file()
    {
        var ffmpeg = StudioMedia.LocateFfmpeg();
        if (ffmpeg == null) return;
        var dir = StudioMedia.TempDir();
        try
        {
            var media = new StudioMedia(new ConfigurationBuilder().Build());
            var wm = Path.Combine(dir, "wm.png");
            try { await media.CreateWatermarkAsync("Miền Tây Quê Tôi", null, wm, CancellationToken.None); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Python")) { return; } // no Python here
            File.Exists(wm).Should().BeTrue();

            async Task<string> Run(string args)
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ffmpeg, "-hide_banner -y " + args)
                    { UseShellExecute = false, RedirectStandardError = true, StandardErrorEncoding = System.Text.Encoding.UTF8 })!;
                var err = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                p.ExitCode.Should().Be(0, err);
                return err;
            }
            var clip = Path.Combine(dir, "black.mp4"); // plain black clip: any bright pixel in a corner is the watermark
            await Run($"-loglevel error -f lavfi -i color=black:size=720x1280:rate=24 -f lavfi -i sine=f=300 -t 3 -c:v libx264 -pix_fmt yuv420p -c:a aac \"{clip}\"");

            var output = Path.Combine(dir, "final.mp4");
            await media.ComposeAsync(new[] { clip }, null, 0.15, "9:16", "1080p", output, CancellationToken.None, wm, "Miền Tây Quê Tôi");

            // top-left slot during the first 7 s is brighter than an untouched black area of the same frame
            async Task<double> Brightness(string crop)
            {
                var stats = await Run($"-ss 1 -i \"{output}\" -frames:v 1 -vf \"crop={crop},signalstats,metadata=print\" -f null -");
                var m = System.Text.RegularExpressions.Regex.Match(stats, @"YAVG=([\d.]+)");
                m.Success.Should().BeTrue(stats);
                return double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            (await Brightness("300:60:130:168")).Should().BeGreaterThan(await Brightness("300:60:390:900") + 10);

            var probe = await Run($"-i \"{output}\" -f null -");
            probe.Should().Contain("Miền Tây Quê Tôi");
        }
        finally
        {
            StudioMedia.TryDelete(dir);
        }
    }

    [Fact]
    public void Watermark_moves_between_three_safe_spots()
    {
        var f = StudioMedia.WatermarkFilter("9:16", 2, "base", 12.5);
        f.Should().Contain("mod(floor((t+12.500)/7),3)").And.Contain("W-w-W*0.11").And.Contain("H*0.70").And.Contain("H*0.085");
        f.Should().StartWith("[2:v]").And.Contain("[base][wm]overlay");
    }

    [Theory]
    [InlineData("9:16", "720p", 720, 1280)]
    [InlineData("9:16", "1080p", 1080, 1920)]
    [InlineData("16:9", "720p", 1280, 720)]
    [InlineData("9:16", "480p", 480, 854)]
    public void Frame_size_follows_aspect_and_resolution(string aspect, string res, int w, int h) =>
        StudioMedia.FrameSize(aspect, res).Should().Be((w, h));

    [Fact]
    public async Task Start_frame_is_cropped_to_the_video_shape_and_last_frame_is_read()
    {
        var ffmpeg = StudioMedia.LocateFfmpeg();
        if (ffmpeg == null) return;
        var dir = StudioMedia.TempDir();
        try
        {
            async Task Make(string args)
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ffmpeg, "-hide_banner -loglevel error -y " + args)
                    { UseShellExecute = false, RedirectStandardError = true })!;
                await p.WaitForExitAsync();
                p.ExitCode.Should().Be(0, await p.StandardError.ReadToEndAsync());
            }
            var wide = Path.Combine(dir, "wide.png");
            var clip = Path.Combine(dir, "clip.mp4");
            await Make($"-f lavfi -i testsrc=size=1600x900 -frames:v 1 \"{wide}\"");
            await Make($"-f lavfi -i testsrc=size=720x1280:rate=24 -t 2 -c:v libx264 -pix_fmt yuv420p \"{clip}\"");
            var media = new StudioMedia(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

            var frame = await media.PrepareStartFrameAsync(await File.ReadAllBytesAsync(wide), "9:16", CancellationToken.None);
            var last = await media.LastFrameAsync(await File.ReadAllBytesAsync(clip), CancellationToken.None);

            JpegSize(frame).Should().Be((720, 1280)); // landscape picture cropped to portrait, no bars
            JpegSize(last).Should().Be((720, 1280));
        }
        finally
        {
            StudioMedia.TryDelete(dir);
        }
    }

    /// <summary>Width/height from a JPEG's SOF marker.</summary>
    private static (int W, int H) JpegSize(byte[] jpg)
    {
        for (var i = 2; i + 9 < jpg.Length;)
        {
            if (jpg[i] != 0xFF) { i++; continue; }
            var marker = jpg[i + 1];
            if (marker is >= 0xC0 and <= 0xC3) return ((jpg[i + 7] << 8) | jpg[i + 8], (jpg[i + 5] << 8) | jpg[i + 6]);
            i += 2 + ((jpg[i + 2] << 8) | jpg[i + 3]);
        }
        throw new InvalidOperationException("not a JPEG");
    }
}
