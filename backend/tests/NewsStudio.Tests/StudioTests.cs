using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Studio;
using Xunit;

namespace NewsStudio.Tests;

/// <summary>AI video studio: Seedance (Vercel AI Gateway) calls, the character bible, planning, prompts, voices and FFmpeg work.</summary>
public class StudioTests
{
    // ───────────── Seedance through the Vercel AI Gateway ─────────────

    [Fact]
    public void Start_body_is_text_to_video_without_watermark()
    {
        var body = SeedanceVideoClient.BuildStartBody("A judge slams the table", "9:16", "720x1280", 30, 1234);
        body["prompt"]!.GetValue<string>().Should().Be("A judge slams the table");
        body["n"]!.GetValue<int>().Should().Be(1);
        body["aspectRatio"]!.GetValue<string>().Should().Be("9:16");
        body["duration"]!.GetValue<int>().Should().Be(12); // Seedance max
        body["resolution"]!.GetValue<string>().Should().Be("720x1280");
        body["seed"]!.GetValue<int>().Should().Be(1234);
        body["providerOptions"]!["bytedance"]!["watermark"]!.GetValue<bool>().Should().BeFalse();
        body.ContainsKey("image").Should().BeFalse();
        SeedanceVideoClient.BuildStartBody("x", "16:9", null, 1, null).ContainsKey("resolution").Should().BeFalse();
    }

    [Theory]
    [InlineData("9:16", "720p", "720x1280")]
    [InlineData("16:9", "1080p", "1920x1080")]
    [InlineData("9:16", "480p", "480x864")]
    public void Seedance_frame_sizes(string aspect, string res, string expected) =>
        SeedanceVideoClient.FrameSize(aspect, res).Should().Be(expected);

    [Theory]
    [InlineData("""{"status":"pending"}""", "pending", null, null, null)]
    [InlineData("""{"status":"completed","videos":[{"type":"url","url":"https://blob.test/v.mp4","mediaType":"video/mp4"}]}""", "completed", "https://blob.test/v.mp4", null, null)]
    [InlineData("""{"status":"completed","videos":[{"type":"base64","data":"AAAA","mediaType":"video/mp4"}]}""", "completed", null, "AAAA", null)]
    [InlineData("""{"status":"error","error":"The request failed because the output video may contain sensitive information"}""", "error", null, null, "The request failed because the output video may contain sensitive information")]
    public void Status_is_read(string json, string status, string? url, string? data, string? error)
    {
        var s = SeedanceVideoClient.ParseStatus(json);
        s.Status.Should().Be(status);
        s.VideoUrl.Should().Be(url);
        s.VideoBase64.Should().Be(data);
        s.Error.Should().Be(error);
    }

    [Fact]
    public void Prices_follow_model_and_resolution()
    {
        var client = Client(new FakeGateway());
        client.EstimateCost(SeedanceVideoClient.FastModel, "480p", 10).Should().Be(0.097m);
        client.EstimateCost(SeedanceVideoClient.ProModel, "480p", 10).Should().Be(0.20m);
        client.PricePerSecond(SeedanceVideoClient.FastModel, "1080p").Should().BeGreaterThan(client.PricePerSecond(SeedanceVideoClient.FastModel, "720p"));
        client.Models().Select(m => m.Id).Should().Equal(SeedanceVideoClient.FastModel, SeedanceVideoClient.ProModel);
        SeedanceVideoClient.NormalizeModel("grok-imagine-video-1.5-lite").Should().Be(SeedanceVideoClient.FastModel);
    }

    [Fact]
    public async Task Job_is_started_polled_and_downloaded()
    {
        var gw = new FakeGateway();
        gw.Polls.Enqueue("""{"status":"pending"}""");
        gw.Polls.Enqueue("""{"status":"completed","videos":[{"type":"url","url":"https://blob.test/op1.mp4","mediaType":"video/mp4"}]}""");
        string? stored = null;

        var clip = await Client(gw).GenerateAsync(SeedanceVideoClient.FastModel, "prompt", "9:16", "720p", 6, 42, CancellationToken.None,
            onStarted: op => { stored = op; return Task.CompletedTask; });

        clip.Video.Should().Equal(FakeGateway.VideoBytes);
        clip.CostUsd.Should().Be(Math.Round(6 * 0.0216m, 4));
        stored.Should().Be("""{"id":"op1"}"""); // kept on the shot the moment the job is paid for
        gw.Starts.Should().Be(1);
        gw.Headers["authorization"].Should().Be("Bearer test-gateway-key");
        gw.Headers["ai-model-id"].Should().Be(SeedanceVideoClient.FastModel);
        gw.Headers["ai-video-model-specification-version"].Should().Be("4");
        gw.Headers["ai-gateway-protocol-version"].Should().Be("0.0.1");
        gw.StartBody!["seed"]!.GetValue<int>().Should().Be(42);
    }

    [Fact]
    public async Task A_refused_resolution_format_is_retried_without_paying()
    {
        var gw = new FakeGateway { RefuseFrameSizeResolution = true };
        gw.Polls.Enqueue("""{"status":"completed","videos":[{"type":"base64","data":"__VIDEO__"}]}""");
        var clip = await Client(gw).GenerateAsync(SeedanceVideoClient.ProModel, "p", "9:16", "1080p", 5, null, CancellationToken.None);
        clip.Video.Should().Equal(FakeGateway.VideoBytes);
        gw.StartResolutions.Should().Equal("1080x1920", "1080p"); // WxH refused (not billed) → "1080p" accepted
    }

    [Fact]
    public async Task A_paid_job_is_resumed_instead_of_paying_again()
    {
        var gw = new FakeGateway();
        gw.Old["old1"] = """{"status":"completed","videos":[{"type":"url","url":"https://blob.test/old1.mp4"}]}""";
        var clip = await Client(gw).GenerateAsync(SeedanceVideoClient.FastModel, "p", "9:16", "720p", 6, 1, CancellationToken.None, resumeOperation: """{"id":"old1"}""");
        gw.Starts.Should().Be(0);
        clip.CostUsd.Should().Be(0m);
        clip.Video.Should().Equal(FakeGateway.VideoBytes);
    }

    [Fact]
    public async Task Recover_never_starts_a_new_job()
    {
        var gw = new FakeGateway();
        gw.Old["paid"] = """{"status":"completed","videos":[{"type":"url","url":"https://blob.test/paid.mp4"}]}""";
        gw.Old["failed"] = """{"status":"error","error":"boom"}""";
        var client = Client(gw);
        (await client.TryRecoverAsync(SeedanceVideoClient.FastModel, """{"id":"paid"}""", CancellationToken.None))!.Value.Bytes.Should().Equal(FakeGateway.VideoBytes);
        (await client.TryRecoverAsync(SeedanceVideoClient.FastModel, """{"id":"failed"}""", CancellationToken.None)).Should().BeNull();
        (await client.TryRecoverAsync(SeedanceVideoClient.FastModel, """{"id":"unknown"}""", CancellationToken.None)).Should().BeNull();
        gw.Starts.Should().Be(0);
    }

    [Theory]
    [InlineData(401, "authentication_error", "Invalid API key")]
    [InlineData(402, "invalid_request_error", "Payment required")]
    [InlineData(400, "invalid_request_error", "Insufficient credits: video generation requires at least $10")]
    public async Task An_unpaid_account_stops_the_run(int status, string type, string message)
    {
        var gw = new FakeGateway { StartStatus = status, StartErrorType = type, StartError = message };
        var act = () => Client(gw).GenerateAsync(SeedanceVideoClient.FastModel, "p", "9:16", "720p", 5, null, CancellationToken.None);
        (await act.Should().ThrowAsync<VideoAccountException>()).Which.Message.Should().Contain("AI_GATEWAY_API_KEY");
    }

    [Fact]
    public async Task Busy_gateway_is_transient()
    {
        var gw = new FakeGateway { StartStatus = 429, StartErrorType = "rate_limit_exceeded", StartError = "slow down" };
        var act = () => Client(gw).GenerateAsync(SeedanceVideoClient.FastModel, "p", "9:16", "720p", 5, null, CancellationToken.None);
        await act.Should().ThrowAsync<VideoTransientException>();
    }

    [Fact]
    public async Task Moderated_clip_is_a_rejection()
    {
        var gw = new FakeGateway();
        gw.Polls.Enqueue("""{"status":"error","error":"The output video may contain sensitive information"}""");
        var act = () => Client(gw).GenerateAsync(SeedanceVideoClient.FastModel, "p", "9:16", "720p", 5, null, CancellationToken.None);
        await act.Should().ThrowAsync<VideoRejectedException>();
    }

    private static SeedanceVideoClient Client(FakeGateway gw)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:Gateway:ApiKey"] = "test-gateway-key", ["Ai:Gateway:BaseUrl"] = "https://gw.test/v4/ai" }).Build();
        return new SeedanceVideoClient(new HttpClient(gw), config, Microsoft.Extensions.Logging.Abstractions.NullLogger<SeedanceVideoClient>.Instance)
        {
            PollInterval = TimeSpan.FromMilliseconds(1)
        };
    }

    /// <summary>Stands in for the Vercel AI Gateway: start → operation {"id":"op1"}, status answers from a queue, file download.</summary>
    private sealed class FakeGateway : HttpMessageHandler
    {
        public static readonly byte[] VideoBytes = Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray();
        public Queue<string> Polls { get; } = new();
        public Dictionary<string, string> Old { get; } = new();
        public int Starts { get; private set; }
        public int StartStatus { get; set; } = 200;
        public string StartErrorType { get; set; } = "internal_server_error";
        public string StartError { get; set; } = "busy";
        public bool RefuseFrameSizeResolution { get; set; }
        public List<string?> StartResolutions { get; } = new();
        public JsonObject? StartBody { get; private set; }
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith("https://blob.test/"))
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(VideoBytes) { Headers = { ContentType = new("video/mp4") } } };
            foreach (var h in request.Headers) Headers[h.Key] = string.Join(",", h.Value);
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            if (url == "https://gw.test/v4/ai/video-model/start")
            {
                var res = body["resolution"]?.GetValue<string>();
                StartResolutions.Add(res);
                if (StartStatus != 200) return Error(StartStatus, StartErrorType, StartError);
                if (RefuseFrameSizeResolution && res != null && res.Contains('x'))
                    return Error(400, "invalid_request_error", "Unsupported resolution for this model");
                Starts++;
                StartBody = body;
                return Json("""{"operation":{"id":"op1"}}""");
            }
            if (url == "https://gw.test/v4/ai/video-model/status")
            {
                var id = body["operation"]!["id"]!.GetValue<string>();
                if (id == "op1") return Json(Polls.Count > 0 ? Polls.Dequeue().Replace("__VIDEO__", Convert.ToBase64String(VideoBytes)) : """{"status":"pending"}""");
                return Old.TryGetValue(id, out var old) ? Json(old) : Error(404, "not_found", "unknown operation");
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) =>
            new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Error(int status, string type, string message) =>
            new((System.Net.HttpStatusCode)status) { Content = new StringContent(JsonSerializer.Serialize(new { error = new { message, type } })) };
    }

    // ───────────── Sound effects (fal.ai MMAudio) ─────────────

    [Fact]
    public void Sfx_request_keeps_voices_and_music_out()
    {
        var prompt = SoundEffectsClient.BuildPrompt("wind through bamboo, birds chirping.", "A farmer walks along the canal");
        prompt.Should().Be("wind through bamboo, birds chirping. A farmer walks along the canal. Realistic sound effects and ambience only.");
        var body = SoundEffectsClient.BuildRequest("https://app.test/api/studio/shots/1/clip", prompt, 45, 70000);
        body["video_url"]!.GetValue<string>().Should().Be("https://app.test/api/studio/shots/1/clip");
        body["negative_prompt"]!.GetValue<string>().Should().Contain("speech").And.Contain("music");
        body["duration"]!.GetValue<double>().Should().Be(30); // model max
        body["seed"]!.GetValue<int>().Should().Be(70000 % 65536);
        SoundEffectsClient.BuildPrompt(null, "").Should().StartWith("natural ambient sound");
    }

    [Theory]
    [InlineData("""{"video":{"url":"https://fal.media/out.mp4","content_type":"application/octet-stream"}}""", "https://fal.media/out.mp4")]
    [InlineData("""{"audio":{"url":"https://fal.media/out.wav"}}""", "https://fal.media/out.wav")]
    [InlineData("""{"other":1}""", null)]
    public void Sfx_result_file_is_found(string json, string? url) => SoundEffectsClient.ParseResultUrl(json).Should().Be(url);

    [Fact]
    public async Task Sfx_job_is_queued_polled_and_downloaded()
    {
        var fal = new FakeFal();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:Fal:ApiKey"] = "test-fal-key" }).Build();
        var client = new SoundEffectsClient(new HttpClient(fal), config, Microsoft.Extensions.Logging.Abstractions.NullLogger<SoundEffectsClient>.Instance)
        {
            PollInterval = TimeSpan.FromMilliseconds(1)
        };
        var bytes = await client.GenerateAsync(null, new byte[] { 1, 2, 3 }, "rain", 6, 1, CancellationToken.None);

        bytes.Should().Equal(FakeFal.Output);
        fal.Auth.Should().OnlyContain(a => a == "Key test-fal-key");
        fal.Submitted!["video_url"]!.GetValue<string>().Should().Be("data:video/mp4;base64,AQID"); // no public URL → inline
        fal.Polls.Should().Be(2);
        client.EstimateCost(10).Should().Be(0.01m);
    }

    private sealed class FakeFal : HttpMessageHandler
    {
        public static readonly byte[] Output = { 9, 8, 7, 6 };
        public JsonObject? Submitted { get; private set; }
        public int Polls { get; private set; }
        public List<string> Auth { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            if (url == "https://fal.media/out.mp4") return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(Output) };
            Auth.Add(request.Headers.Authorization?.ToString() ?? "");
            if (request.Method == HttpMethod.Post && url == "https://queue.fal.run/fal-ai/mmaudio-v2")
            {
                Submitted = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
                return Json("""{"request_id":"r1","status_url":"https://queue.fal.run/fal-ai/mmaudio-v2/requests/r1/status","response_url":"https://queue.fal.run/fal-ai/mmaudio-v2/requests/r1"}""");
            }
            if (url.EndsWith("/requests/r1/status"))
                return Json(++Polls < 2 ? """{"status":"IN_PROGRESS"}""" : """{"status":"COMPLETED"}""");
            if (url.EndsWith("/requests/r1")) return Json("""{"video":{"url":"https://fal.media/out.mp4"}}""");
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) =>
            new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task Music_ducks_under_speech_but_not_under_sound_effects()
    {
        var ffmpeg = StudioMedia.LocateFfmpeg();
        if (ffmpeg == null) return;
        var dir = StudioMedia.TempDir();
        try
        {
            async Task<string> Run(string args)
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ffmpeg, "-hide_banner -y " + args)
                    { UseShellExecute = false, RedirectStandardError = true, StandardErrorEncoding = System.Text.Encoding.UTF8 })!;
                var err = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                p.ExitCode.Should().Be(0, err);
                return err;
            }
            var a = Path.Combine(dir, "a.mp4");
            var b = Path.Combine(dir, "b.mp4");
            var voice = Path.Combine(dir, "voice.m4a");   // speech stand-in: 300 Hz
            var fx = Path.Combine(dir, "fx.m4a");         // effects stand-in: 3 kHz, loud
            var music = Path.Combine(dir, "music.m4a");   // music stand-in: 1 kHz
            await Run($"-loglevel error -f lavfi -i testsrc=size=720x1280:rate=24 -t 4 -c:v libx264 -pix_fmt yuv420p \"{a}\"");
            await Run($"-loglevel error -f lavfi -i testsrc=size=720x1280:rate=24 -t 4 -c:v libx264 -pix_fmt yuv420p \"{b}\"");
            await Run($"-loglevel error -f lavfi -i sine=f=300:sample_rate=48000 -t 4 -c:a aac \"{voice}\"");
            await Run($"-loglevel error -f lavfi -i sine=f=3000:sample_rate=48000 -t 4 -c:a aac \"{fx}\"");
            await Run($"-loglevel error -f lavfi -i sine=f=1000:sample_rate=48000 -t 10 -c:a aac \"{music}\"");

            var media = new StudioMedia(new ConfigurationBuilder().Build());
            var output = Path.Combine(dir, "final.mp4");
            await media.ComposeAsync(new[] { a, b }, music, 0.5, "9:16", "720p", output, CancellationToken.None,
                voices: new string?[] { voice, null }, effects: new string?[] { null, fx }, effectsVolume: 1.0);

            async Task<double> MusicLevel(double from)
            {
                var log = await Run($"-ss {from.ToString(System.Globalization.CultureInfo.InvariantCulture)} -t 1.5 -i \"{output}\" -af \"bandpass=f=1000:width_type=q:w=8,volumedetect\" -f null -");
                return double.Parse(System.Text.RegularExpressions.Regex.Match(log, @"mean_volume: (-?[\d.]+) dB").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            var underSpeech = await MusicLevel(1.5);   // clip a: voice talking
            var underEffects = await MusicLevel(5.5);  // clip b: effects only
            underEffects.Should().BeGreaterThan(underSpeech + 4, "music is pushed down only while someone speaks");
            (await Run($"-i \"{output}\" -f null -")).Should().Contain("Audio:");
        }
        finally
        {
            StudioMedia.TryDelete(dir);
        }
    }

    // ───────────── Script library import (shot for shot, no planning) ─────────────

    private static StudioScriptImportDto LibraryScript() => new()
    {
        Title = "Nồi Cơm Độn Sắn Của Mẹ", Code = "BCN-01", Mood = "Nghẹn ngào rớt nước mắt", Region = "Bắc Bộ", Era = "8x",
        Hook = "Có những đứa trẻ lớn lên bằng nồi cơm độn sắn mốc, để rồi cả đời đi xa vẫn không sao quên được giọt nước mắt của mẹ bên bếp củi chiều mưa...",
        Ending = "Bây giờ mâm cao cỗ đầy, nhưng con tìm đâu lại được một hạt cơm trắng ngập tràn tình mẹ năm xưa...",
        Scenes =
        {
            new StudioSceneImportDto
            {
                Title = "Khói bếp lam chiều", DurationSeconds = 15, Camera = "slow wide cinematic establishing shot",
                PromptEn = "Cinematic 1994 rural Vietnam 35mm film still, Kodak Portra 400. A rustic mud-brick kitchen hut in heavy rain. Photorealistic 8k, raw emotional atmosphere.",
                Voiceover = "Những năm chín mươi ấy, nhà nào ở quê cũng nghèo rớt mồng tơi. Bữa cơm chiều tháng ba giáp hạt bao giờ cũng đẫm mùi khói rơm rạ và vị sắn khô cay nồng...",
                Sound = "Tiếng mưa rào rơi lộp độp trên lá chuối ngoài hiên"
            },
            new StudioSceneImportDto
            {
                Title = "Bát cơm của mẹ", DurationSeconds = 30, Camera = "slow push-in medium close-up",
                PromptEn = "Emotional medium close-up: the mother scraping the scarce white rice into her son's bowl.",
                Voiceover = "Mẹ khẽ cười, gạt hết những hạt cơm trắng hiếm hoi sang bát con, bảo: \"Con ăn đi cho chóng lớn, mẹ thích ăn sắn hơn\". Mãi sau này lớn khôn con mới hiểu, mẹ chưa bao giờ thích ăn sắn...",
                Sound = "Tiếng mẹ khẽ thở dài nghẹn ngào"
            },
        }
    };

    [Fact]
    public void Library_script_becomes_clip_sized_shots_that_keep_every_word()
    {
        var script = LibraryScript();
        var shots = StudioScriptImporter.BuildShots(script);

        shots.Should().OnlyContain(s => s.Duration >= 2 && s.Duration <= 12);                      // every shot fits one Seedance clip
        shots.Where(s => s.Dialogue != null).Should().OnlyContain(s => StudioService.SecondsToSay(s.Dialogue) <= 12);
        string Words(string t) => System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim();
        var spoken = Words(string.Join(" ", shots.Where(s => s.Dialogue != null).Select(s => s.Dialogue)));
        spoken.Should().StartWith("Có những đứa trẻ lớn lên");                                          // hook opens the video
        spoken.Should().EndWith("ngập tràn tình mẹ năm xưa...");                                        // ending closes it
        spoken.Should().Contain(Words(script.Scenes[0].Voiceover!)).And.Contain(Words(script.Scenes[1].Voiceover!)); // nothing dropped
        shots.Where(s => s.Scene == 1).Should().HaveCountGreaterThanOrEqualTo(3);                       // a 30 s scene → several clips
        shots.Where(s => s.Scene == 1).Select(s => s.Camera).Distinct().Should().HaveCountGreaterThan(1); // varied framing
        shots.First().Camera.Should().Be("slow wide cinematic establishing shot");                    // the script's own camera first
        shots.First().Action.Should().Contain("35mm film footage").And.NotContain("film still").And.NotContain("8k");
        shots.Should().OnlyContain(s => s.Sound != null);
    }

    [Fact]
    public void Narration_splits_on_sentences_then_commas_never_mid_word()
    {
        var text = string.Join(" ", Enumerable.Repeat("Một hai ba bốn năm sáu bảy tám chín mười.", 5));
        var pieces = StudioScriptImporter.SplitNarration(text, 28);
        pieces.Should().OnlyContain(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 28);
        string.Join(" ", pieces).Should().Be(text);
        StudioScriptImporter.SplitNarration(string.Join(", ", Enumerable.Repeat("chữ chữ chữ chữ chữ chữ chữ chữ chữ chữ", 6)), 28)
            .Should().OnlyContain(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 28);
        StudioScriptImporter.SplitNarration("  ").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Nghẹn ngào rớt nước mắt", "gemini:Sulafat", "-8%")]
    [InlineData("Da diết nhớ cha mẹ", "gemini:Charon", "-6%")]
    [InlineData("Hóm hỉnh ngây ngô", "gemini:Puck", "+0%")]
    [InlineData("Ấm áp hoài niệm", "gemini:Aoede", "-4%")]
    public void Narrator_voice_follows_the_mood(string mood, string voice, string speed)
    {
        var n = StudioScriptImporter.NarratorFor(mood);
        n.Voice.Should().Be(voice);
        n.Speed.Should().Be(speed);
        StudioVoices.IsKnown(n.Voice).Should().BeTrue();
    }

    [Theory]
    [InlineData("-8%", "-8%")]
    [InlineData("5", "+5%")]
    [InlineData("-50%", "-20%")]
    [InlineData("fast", "+0%")]
    public void Speed_is_normalized(string input, string expected) => StudioVoices.NormalizeSpeed(input).Should().Be(expected);

    [Fact]
    public void Prepare_answer_must_translate_every_sound()
    {
        var song = Guid.NewGuid();
        var ok = StudioService.ParsePrepare($$"""{"sounds":["rain on banana leaves","a soft sigh"],"caption":"Nồi cơm của mẹ #kyuc","musicTrackId":"{{song}}"}""", 2, new HashSet<Guid> { song });
        ok.Sounds.Should().Equal("rain on banana leaves", "a soft sigh");
        ok.MusicTrackId.Should().Be(song);
        var act = () => StudioService.ParsePrepare("""{"sounds":["one"]}""", 2, new HashSet<Guid>());
        act.Should().Throw<InvalidOperationException>();
    }

    // ───────────── Bible, planning, prompts ─────────────

    [Fact]
    public void Plan_builds_the_bible_and_keeps_cast_names_from_it()
    {
        var song = Guid.NewGuid();
        var plan = StudioService.ParsePlan($$"""
            Kế hoạch: {"caption":"Đại nhân sợ vợ","notes":"hài","musicTrackId":"{{song}}",
             "characters":[{"name":"Bao Công","gender":"male","look":"stout judge, dark face, crescent mark","voice":"gemini:Orus"},
                           {"name":"Phu nhân","gender":"female","look":"elegant lady in brocade","voice":"gemini:Orus"},
                           {"name":"Lời dẫn","gender":"male","look":"","voice":"gemini:Unknown"}],
             "locations":[{"name":"Công đường","look":"Song dynasty court hall"}],
             "shots":[{"duration":3,"location":"công đường","cast":["bao công","Ai Đó","Lời dẫn"],"action":"He slams the table","camera":"wide shot",
                       "speaker":"Bao Công","dialogue":"Bãi đường! Thả phạm nhân! Ta có vụ án gia đình cần xử gấp!","sound":"gavel"},
                      {"duration":40,"location":"Hậu viện","cast":[],"action":"Courtyard at dusk","camera":"static","speaker":"null","dialogue":null},
                      {"duration":1,"action":"","dialogue":null}]}
            """, new HashSet<Guid> { song });

        plan.MusicTrackId.Should().Be(song);
        plan.Bible.Characters.Select(c => c.Name).Should().Equal("Bao Công", "Phu nhân", "Lời dẫn");
        plan.Bible.Characters[0].Voice.Should().Be("gemini:Orus");
        plan.Bible.Characters[1].Voice.Should().NotBe("gemini:Orus").And.StartWith("gemini:"); // same voice twice → re-picked
        StudioVoices.All.Single(v => v.Id == plan.Bible.Characters[1].Voice).Gender.Should().Be("female");
        StudioVoices.IsKnown(plan.Bible.Characters[2].Voice).Should().BeTrue();          // unknown voice → a real one
        plan.Shots.Should().HaveCount(2);                                                // empty shot dropped
        plan.Shots[0].Cast.Should().Equal("Bao Công");                                   // canonical name; stranger + narrator dropped
        plan.Shots[0].Location.Should().Be("Công đường");
        plan.Shots[0].Duration.Should().Be(6);                                           // 14 words → 6 s, not 3
        plan.Shots[1].Duration.Should().Be(12);                                          // Seedance max
        plan.Shots[1].Speaker.Should().BeNull();
    }

    [Fact]
    public void Plan_with_unknown_music_falls_back_to_none()
    {
        var plan = StudioService.ParsePlan("""{"musicTrackId":"not-a-guid","shots":[{"duration":8,"action":"x"}]}""", new HashSet<Guid>());
        plan.MusicTrackId.Should().BeNull();
    }

    [Fact]
    public void Plan_prompt_is_text_to_video_with_the_voice_list()
    {
        var (system, user) = StudioService.BuildPlanPrompt(new StudioVideo { Script = "Bao Công: Án mạng à?", TargetDurationSeconds = 180 }, new List<StudioMusicTrack>());
        system.Should().Contain("TEXT-TO-VIDEO").And.Contain("KHÔNG có tiếng");
        user.Should().Contain("gemini:Charon").And.Contain("\"characters\"").And.Contain("\"locations\"").And.Contain("\"cast\"");
        user.Should().Contain("KHÔNG thêm shot độn").And.Contain("số nguyên 2–12").And.NotContain("180");
    }

    [Fact]
    public void Shot_prompt_copies_the_bible_looks_and_dubs_the_speaker()
    {
        var bible = StudioService.NormalizeBible(
            new[]
            {
                new StudioCharacterDto { Name = "Bao Công", Gender = "male", Look = "stout judge, dark face, crescent mark, purple robe" },
                new StudioCharacterDto { Name = "Công Tôn Sách", Gender = "male", Look = "thin advisor, goatee, black hat" },
            },
            new[] { new StudioLocationDto { Name = "Công đường", Look = "Song dynasty court hall, red pillars, morning light" } });
        var shot = new StudioShot
        {
            Location = "Công đường", CastJson = JsonSerializer.Serialize(new[] { "Bao Công", "Công Tôn Sách" }), Action = "The advisor rushes in",
            Camera = "wide shot", Speaker = "Công Tôn Sách", Dialogue = "Bẩm đại nhân! Phu nhân gửi thư khẩn!"
        };
        var prompt = StudioService.BuildShotPrompt(new StudioVideo { AspectRatio = "9:16", StyleNotes = "comedy" }, bible, shot);

        prompt.Should().Contain("vertical 9:16").And.Contain("Setting: Song dynasty court hall, red pillars, morning light");
        prompt.Should().Contain("Characters in frame (2, each appears exactly once)").And.Contain("Bao Công — stout judge, dark face, crescent mark, purple robe");
        prompt.Should().Contain("Công Tôn Sách is talking").And.Contain("no extreme close-up of the mouth");
        prompt.Should().NotContain("Bẩm đại nhân"); // silent video: the line is dubbed, never put in the picture
        prompt.Should().Contain("No text, no subtitles");

        var narrated = StudioService.BuildShotPrompt(new StudioVideo(), bible, new StudioShot { Action = "Night falls", Speaker = "Lời dẫn", Dialogue = "Đêm đó…" });
        narrated.Should().Contain("Nobody is talking");
    }

    [Fact]
    public void Voices_follow_the_speaker()
    {
        var bible = StudioService.NormalizeBible(new[]
        {
            new StudioCharacterDto { Name = "Phu nhân", Gender = "female", Voice = "gemini:Gacrux" },
            new StudioCharacterDto { Name = "Lời dẫn", Gender = "male", Voice = "gemini:Sadaltager" },
        }, null);
        StudioService.VoiceFor(bible, "phu nhân").Should().Be("gemini:Gacrux");
        StudioService.VoiceFor(bible, null).Should().Be("gemini:Sadaltager");      // narrator
        StudioService.VoiceFor(bible, "Người lạ").Should().Be(StudioVoices.DefaultNarratorVoice);
        StudioVoices.Pick("female", new[] { "gemini:Kore" }).Should().NotBe("gemini:Kore");
    }

    [Fact]
    public void Plan_gives_every_line_enough_time_to_be_said()
    {
        StudioService.SecondsToSay("Còn nghiêm trọng hơn ạ! Phu nhân bảo nếu đại nhân không về ngay thì tối nay ngủ ngoài cửa!").Should().Be(8);
        StudioService.SecondsToSay(null).Should().Be(0);
    }

    [Theory]
    [InlineData("Cá lóc nướng thơm lừng!", "Cá lóc nướng thơm lừng!\n\n🤖 Video được tạo bằng AI.")]
    [InlineData("Phim ngắn (tạo bằng AI)", "Phim ngắn (tạo bằng AI)")]
    [InlineData(null, "🤖 Video được tạo bằng AI.")]
    public void Caption_always_discloses_ai(string? caption, string expected) =>
        StudioService.CaptionWithDisclosure(caption).Should().Be(expected);

    [Fact]
    public async Task Compose_dubs_each_silent_clip_with_its_line()
    {
        var ffmpeg = StudioMedia.LocateFfmpeg();
        if (ffmpeg == null) return;
        var dir = StudioMedia.TempDir();
        try
        {
            async Task<string> Run(string args)
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ffmpeg, "-hide_banner -y " + args)
                    { UseShellExecute = false, RedirectStandardError = true, StandardErrorEncoding = System.Text.Encoding.UTF8 })!;
                var err = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                p.ExitCode.Should().Be(0, err);
                return err;
            }
            var a = Path.Combine(dir, "a.mp4"); // Seedance-like: video only, no audio stream
            var b = Path.Combine(dir, "b.mp4");
            var line = Path.Combine(dir, "line.m4a");
            await Run($"-loglevel error -f lavfi -i testsrc=size=720x1280:rate=24 -t 3 -c:v libx264 -pix_fmt yuv420p \"{a}\"");
            await Run($"-loglevel error -f lavfi -i testsrc=size=720x1280:rate=24 -t 2 -c:v libx264 -pix_fmt yuv420p \"{b}\"");
            await Run($"-loglevel error -f lavfi -i sine=f=440 -t 1.5 -c:a aac \"{line}\"");

            var media = new StudioMedia(new ConfigurationBuilder().Build());
            var output = Path.Combine(dir, "final.mp4");
            var seconds = await media.ComposeAsync(new[] { a, b }, null, 0.15, "9:16", "720p", output, CancellationToken.None, voices: new string?[] { line, null });

            seconds.Should().BeApproximately(5.0, 0.3); // clip lengths rule, the voice never stretches them
            var loud = await Run($"-ss 0.5 -t 1 -i \"{output}\" -af volumedetect -f null -");
            var quiet = await Run($"-ss 3.5 -t 1 -i \"{output}\" -af volumedetect -f null -");
            double Mean(string log) => double.Parse(System.Text.RegularExpressions.Regex.Match(log, @"mean_volume: (-?[\d.]+) dB").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Mean(loud).Should().BeGreaterThan(Mean(quiet) + 20); // first clip carries the line, second is silent
        }
        finally
        {
            StudioMedia.TryDelete(dir);
        }
    }

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
