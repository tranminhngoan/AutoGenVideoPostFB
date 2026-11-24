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

    // ───────────── Sound effects (ElevenLabs through the Vercel AI Gateway) ─────────────

    [Fact]
    public void Sfx_prompt_uses_the_script_sound_notes()
    {
        SoundEffectsClient.BuildPrompt("rain pattering on banana leaves, a wood fire crackling.", "A mother in a mud kitchen")
            .Should().Be("rain pattering on banana leaves, a wood fire crackling");
        SoundEffectsClient.BuildPrompt(null, "A farmer walks along the canal at dusk").Should().StartWith("soft natural ambience for this scene: A farmer walks");
        var body = SoundEffectsClient.BuildRequest("rain", 40);
        body["text"]!.GetValue<string>().Should().Be("rain");
        body["outputFormat"]!.GetValue<string>().Should().Be("mp3");
        body["providerOptions"]!["elevenlabs"]!["durationSeconds"]!.GetValue<double>().Should().Be(22); // model max
        SoundEffectsClient.BuildRequest("rain", 6.4)["providerOptions"]!["elevenlabs"]!["durationSeconds"]!.GetValue<double>().Should().Be(6.4);
    }

    [Fact]
    public async Task Sfx_goes_through_the_gateway_speech_endpoint()
    {
        var gw = new FakeSpeechGateway();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Ai:Gateway:ApiKey"] = "test-gateway-key", ["Ai:Gateway:BaseUrl"] = "https://gw.test/v4/ai" }).Build();
        var client = new SoundEffectsClient(new HttpClient(gw), config, Microsoft.Extensions.Logging.Abstractions.NullLogger<SoundEffectsClient>.Instance);

        var audio = await client.GenerateAsync("frogs croaking at dusk", 8, CancellationToken.None);

        audio.Should().Equal(FakeSpeechGateway.Audio);
        gw.Url.Should().Be("https://gw.test/v4/ai/speech-model");
        gw.Headers["authorization"].Should().Be("Bearer test-gateway-key");
        gw.Headers["ai-model-id"].Should().Be("elevenlabs/sound-effects-v2");
        gw.Headers["ai-speech-model-specification-version"].Should().Be("4");
        gw.Body!["text"]!.GetValue<string>().Should().Be("frogs croaking at dusk");
        client.IsConfigured.Should().BeTrue(); // same key as the video clips
        client.EstimateCost(10).Should().Be(0.02m);
    }

    private sealed class FakeSpeechGateway : HttpMessageHandler
    {
        public static readonly byte[] Audio = Enumerable.Range(0, 2048).Select(i => (byte)(i * 7)).ToArray();
        public string? Url { get; private set; }
        public JsonObject? Body { get; private set; }
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.ToString();
            foreach (var h in request.Headers) Headers[h.Key] = string.Join(",", h.Value);
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            var json = JsonSerializer.Serialize(new { audio = Convert.ToBase64String(Audio), warnings = Array.Empty<object>() });
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task An_mp3_effects_track_is_turned_into_compact_aac()
    {
        var ffmpeg = StudioMedia.LocateFfmpeg();
        if (ffmpeg == null) return;
        var dir = StudioMedia.TempDir();
        try
        {
            var mp3 = Path.Combine(dir, "fx.mp3");
            using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ffmpeg,
                       $"-hide_banner -loglevel error -y -f lavfi -i anoisesrc=d=3:c=pink -c:a libmp3lame \"{mp3}\"") { UseShellExecute = false })!)
                await p.WaitForExitAsync();
            var media = new StudioMedia(new ConfigurationBuilder().Build());
            var aac = await media.ExtractAudioAsync(await File.ReadAllBytesAsync(mp3), CancellationToken.None, ".mp3");
            aac.Length.Should().BeGreaterThan(1000);
            (await media.ProbeSecondsAsync(aac, ".m4a", CancellationToken.None)).Should().BeApproximately(3, 0.3);
        }
        finally
        {
            StudioMedia.TryDelete(dir);
        }
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
            await Run($"-loglevel error -f lavfi -i sine=f=300:sample_rate=48000 -t 4 -af volume=6 -c:a aac \"{voice}\""); // as loud as real narration
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
    [InlineData("Da diết nhớ cha mẹ", "gemini:Sulafat", "-6%")]
    [InlineData("Hóm hỉnh ngây ngô", "gemini:Puck", "+0%")]
    [InlineData("Ấm áp hoài niệm", "gemini:Sulafat", "-4%")]
    public void Narrator_voice_follows_the_mood(string mood, string voice, string speed)
    {
        var n = StudioScriptImporter.NarratorFor(mood);
        n.Voice.Should().Be(voice);
        n.Speed.Should().Be(speed);
        StudioVoices.IsKnown(n.Voice).Should().BeTrue();
    }

    [Theory]
    [InlineData(11.4, true, 12)]   // the real case: 11.4 s at -8 % — starts at 0.25 s, still fits a 12 s clip
    [InlineData(11.6, true, 12)]
    [InlineData(11.7, false, 12)]
    [InlineData(4.2, true, 5)]     // short line: planned 3 s grows to fit line + breath
    public void A_line_fits_one_clip_up_to_the_last_breath(double voice, bool fits, int clip)
    {
        StudioService.FitsOneClip(voice).Should().Be(fits);
        StudioService.ClipSecondsFor(3, voice).Should().Be(clip);
    }

    [Theory]
    [InlineData("-8%", "-8%")]
    [InlineData("5", "+5%")]
    [InlineData("-50%", "-20%")]
    [InlineData("fast", "+0%")]
    public void Speed_is_normalized(string input, string expected) => StudioVoices.NormalizeSpeed(input).Should().Be(expected);

    [Fact]
    public void Imported_scenes_put_the_words_first_and_any_silent_beat_last()
    {
        var shots = StudioScriptImporter.BuildShots(LibraryScript());
        foreach (var scene in shots.GroupBy(x => x.Scene))
        {
            var lines = scene.Select(x => x.Dialogue != null).ToList();
            lines.SkipWhile(x => x).Should().NotContain(true, "the scene is read in one take: no silent shot between its words");
        }
        StudioScriptImporter.NarratorStyleFor("Nghẹn ngào rớt nước mắt").Should().Contain("tears").And.Contain("never like a newsreader");
    }

    [Fact]
    public void Direction_answer_must_describe_most_shots()
    {
        var song = Guid.NewGuid();
        var json = $$"""
            {"caption":"Nồi cơm của mẹ #kyuc","musicTrackId":"{{song}}",
             "characters":[{"name":"Mẹ","gender":"female","look":"Vietnamese woman, 38, thin, oval sun-tanned face, hair in a low bun"},
                           {"name":"Lời dẫn","gender":"female","look":"narrator"},
                           {"name":"Bé Tí","gender":"male","look":"Vietnamese boy, 8, skinny, shaved head with a tuft, big dark eyes"}],
             "locations":[{"name":"Bếp tranh","look":"mud-brick kitchen hut with a straw roof, clay stove, soot-black walls"}],
             "scenes":[{"scene":1,"location":"Bếp tranh","time":"late afternoon, heavy rain, dim grey light",
                        "wardrobe":{"Mẹ":"faded brown blouse with patched elbows","Bé Tí":"oversized white shirt"},
                        "voiceStyle":"tender, slow, voice catching","sound":"heavy rain on banana leaves",
                        "shots":[{"shot":1,"cast":["Mẹ","Ai đó","Lời dẫn"],"action":"Mẹ blows on the fire.","camera":"wide shot, slow push-in"},
                                 {"shot":2,"cast":["Bé Tí"],"action":"Bé Tí watches the pot","camera":"medium shot"}]}]}
            """;
        var d = StudioService.ParseDirection(json, 2, new HashSet<Guid> { song });
        d.MusicTrackId.Should().Be(song);
        d.Characters.Select(c => c.Name).Should().Equal("Mẹ", "Bé Tí"); // narrator is not a person in the picture

        var video = new StudioVideo
        {
            BibleJson = JsonSerializer.Serialize(new { characters = new[] { new { name = "Lời dẫn", gender = "female", look = "", voice = "gemini:Sulafat", speed = "-8%" } } },
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        var one = new StudioShot { Index = 1, Scene = 1, Action = "original prompt", Dialogue = "Có những đứa trẻ…", Speaker = "Lời dẫn" };
        var two = new StudioShot { Index = 2, Scene = 1, Action = "original prompt", Dialogue = "Mẹ khẽ cười…", Speaker = "Lời dẫn" };
        video.Shots.Add(one);
        video.Shots.Add(two);
        StudioService.ApplyDirection(video, d);

        var bible = StudioService.ReadBible(video.BibleJson);
        bible.Characters.Select(c => c.Name).Should().Equal("Lời dẫn", "Mẹ", "Bé Tí");
        bible.Characters[0].Voice.Should().Be("gemini:Sulafat");                     // the chosen narrator voice stays
        one.Location.Should().Be("Bếp tranh");
        StudioService.Names(one.CastJson).Should().Equal("Mẹ");                       // unknown + narrator dropped
        one.Action.Should().Be("Mẹ blows on the fire. Wardrobe: Mẹ wears faded brown blouse with patched elbows. Time and light: late afternoon, heavy rain, dim grey light.");
        two.Action.Should().Contain("Bé Tí wears oversized white shirt").And.NotContain("Mẹ wears");  // only who is in frame
        two.Action.Should().Contain("late afternoon, heavy rain");                                    // same light in every shot of the scene
        one.Sound.Should().Be("heavy rain on banana leaves");
        one.VoiceStyle.Should().Be("tender, slow, voice catching");

        var prompt = StudioService.BuildShotPrompt(new StudioVideo { AspectRatio = "9:16" }, bible, one);
        prompt.Should().Contain("Setting: mud-brick kitchen hut").And.Contain("Mẹ — Vietnamese woman, 38").And.Contain("keep every face, hairstyle, body and costume");

        var tooFew = () => StudioService.ParseDirection("""{"scenes":[{"scene":1,"shots":[{"shot":1,"action":"x"}]}]}""", 5, new HashSet<Guid>());
        tooFew.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Ai_music_asks_lyria_for_an_instrumental_and_reads_the_mp3_back()
    {
        LyriaMusicClient.Briefs.Should().HaveCountLessThanOrEqualTo(8).And.OnlyHaveUniqueItems(b => b.Id);
        LyriaMusicClient.Briefs.Should().OnlyContain(b => b.Prompt.Contains("no vocals") && b.Mood.Length > 0);
        LyriaMusicClient.CustomPrompt("nhạc buồn sáo trúc").Should().Contain("\"nhạc buồn sáo trúc\"").And.Contain("no vocals");

        var body = LyriaMusicClient.BuildRequest("lyria-3.5", "sad bamboo flute");
        body["model"]!.GetValue<string>().Should().Be("lyria-3.5");
        body["input"]!.GetValue<string>().Should().Be("sad bamboo flute");
        body["response_format"]!["type"]!.GetValue<string>().Should().Be("audio");

        var mp3 = new byte[] { 0x49, 0x44, 0x33, 4, 0 }.Concat(Enumerable.Repeat((byte)7, 400)).ToArray(); // "ID3…"
        var answer = JsonSerializer.Serialize(new
        {
            steps = new object[]
            {
                new { type = "model_output", content = new object[] { new { type = "text", text = "Intro: flute" }, new { type = "audio", mime_type = "audio/mpeg", data = Convert.ToBase64String(mp3) } } }
            }
        });
        var parsed = LyriaMusicClient.ParseAudio(answer);
        parsed!.Value.Audio.Should().Equal(mp3);
        parsed.Value.Extension.Should().Be(".mp3");
        LyriaMusicClient.ParseAudio("""{"steps":[{"type":"model_output","content":[{"type":"text","text":"no audio"}]}]}""").Should().BeNull();
    }

    [Fact]
    public void Shortened_narration_must_fit_the_budget_and_keep_the_story()
    {
        StudioService.WordBudget(90, 5).Should().Be(212);                 // (90 - 5) s × 2.5 words
        var scenes = new[] { 1, 2, 3, 4, 5 };
        string Words(int n) => string.Join(" ", Enumerable.Repeat("chữ", n));
        string Answer(params (int Scene, int Words)[] x) =>
            JsonSerializer.Serialize(new { scenes = x.Select(s => new { scene = s.Scene, narration = Words(s.Words) }) });

        var ok = StudioService.ParseCondense(Answer((1, 50), (2, 40), (3, 0), (4, 60), (5, 50)), scenes, 213);
        ok[3].Should().BeEmpty();                                          // a minor scene may be dropped
        ok[1].Split(' ').Should().HaveCount(50);

        var tooLong = () => StudioService.ParseCondense(Answer((1, 100), (2, 100), (3, 100)), scenes, 213);
        tooLong.Should().Throw<InvalidOperationException>().WithMessage("*vẫn dài*");
        var tooShort = () => StudioService.ParseCondense(Answer((1, 20), (2, 20)), scenes, 213);
        tooShort.Should().Throw<InvalidOperationException>();
        var oneScene = () => StudioService.ParseCondense(Answer((1, 200)), scenes, 213);
        oneScene.Should().Throw<InvalidOperationException>();

        var (_, user) = StudioService.BuildCondensePrompt(new StudioVideo { Title = "Nồi cơm của mẹ" },
            new[] { (1, (string?)"Khói bếp", "Có những đứa trẻ lớn lên…"), (2, (string?)"Bát cơm", "Mẹ khẽ cười…") }, 213, 90);
        user.Should().Contain("TỐI ĐA 213 chữ").And.Contain("khoảng 90 giây").And.Contain("CẢNH 2 — Bát cơm").And.Contain("\"scenes\"");
    }

    [Fact]
    public void Outfits_and_key_objects_are_written_the_same_in_every_shot()
    {
        var json = """
            {"characters":[{"name":"Bác Bán Kem","gender":"male","look":"62-year-old thin Vietnamese man","outfit":"always wearing a faded olive pith helmet, grey-blue shirt, black plastic sandals"}],
             "props":[{"name":"Thùng kem","look":"a white styrofoam box on the bicycle rear rack; inside, rows of colourful ice pops on wooden sticks"}],
             "locations":[{"name":"Đường làng","look":"dirt lane"}],
             "scenes":[{"scene":1,"location":"Đường làng","time":"noon","wardrobe":{},"sound":"cicadas",
                        "shots":[{"shot":1,"cast":["Bác Bán Kem"],"props":["Thùng kem"],"action":"He pedals slowly","camera":"wide","sound":"a small brass hand bell rung by hand, ting ting"},
                                 {"shot":2,"cast":["Bác Bán Kem"],"props":[],"action":"He wipes his forehead","camera":"medium"}]}]}
            """;
        var d = StudioService.ParseDirection(json, 2, new HashSet<Guid>());
        var video = new StudioVideo();
        var one = new StudioShot { Index = 1, Scene = 1, Action = "x" };
        var two = new StudioShot { Index = 2, Scene = 1, Action = "x" };
        video.Shots.Add(one);
        video.Shots.Add(two);
        StudioService.ApplyDirection(video, d);
        var bible = StudioService.ReadBible(video.BibleJson);

        one.Action.Should().Contain("Key objects: Thùng kem — a white styrofoam box").And.NotContain("Wardrobe:");
        one.Sound.Should().Be("a small brass hand bell rung by hand, ting ting; background: cicadas");  // the bell only where it rings
        two.Sound.Should().Be("cicadas");
        foreach (var shot in new[] { one, two })
            StudioService.BuildShotPrompt(new StudioVideo(), bible, shot).Should()
                .Contain("Bác Bán Kem — 62-year-old thin Vietnamese man, always wearing a faded olive pith helmet")       // hat in every shot
                .And.Contain("not an illustration, painting, cartoon");
    }

    [Fact]
    public void Scene_take_times_each_clip_to_its_words()
    {
        // 3 spoken shots of 10, 20, 10 words read in 13 s: the words' share of the take sets each clip
        var d = StudioService.FitSceneTake(new[] { (10, 4, false), (20, 6, false), (10, 4, false) }, 13);
        d.Should().Equal(4, 6, 4);                          // words end at 3.5 → 4, 10 → 10; 13.25 + breath → 14
        d.Sum().Should().BeGreaterThanOrEqualTo(14);        // the whole take plays inside the scene
        d.Should().OnlyContain(x => x >= 2 && x <= 12);

        // a shot that already has its clip keeps its length; a silent closing beat keeps its own
        StudioService.FitSceneTake(new[] { (10, 8, true), (10, 4, false), (0, 5, false) }, 9).Should().Equal(8, 2, 5);

        // the real case (BCN-03, scene 2): one shot, 11.6 s read slowly — fits one 12 s clip like a single line does
        StudioService.FitSceneTake(new[] { (26, 9, false) }, 11.6).Should().Equal(12);

        // a take the scene can never hold is refused before any clip is paid for
        var tooLong = () => StudioService.FitSceneTake(new[] { (10, 4, false) }, 20);
        tooLong.Should().Throw<InvalidOperationException>().WithMessage("*Thêm 1 shot*");
    }

    [Fact]
    public void A_scene_is_read_in_one_take_and_a_character_line_alone()
    {
        StudioShot Shot(int i, int? scene, string? line, string? speaker = "Lời dẫn") =>
            new() { Id = Guid.NewGuid(), Index = i, Scene = scene, Dialogue = line, Speaker = line == null ? null : speaker };
        var shots = new List<StudioShot>
        {
            Shot(1, 1, null), Shot(2, 1, "Câu một."), Shot(3, 1, "Câu hai."), Shot(4, 1, null),
            Shot(5, 2, "Mẹ ơi!", "Bé Tí"),                 // a character's own line: read on its own
            Shot(6, 2, "Câu ba."),
            Shot(7, null, "Lời của kế hoạch AI."),         // planned video: one line per shot as before
        };
        var takes = StudioService.SceneTakes(shots);
        takes.Select(t => t.Select(s => s.Index)).Should().BeEquivalentTo(new[] { new[] { 1, 2, 3, 4 }, new[] { 6 } }, o => o.WithStrictOrdering());
        StudioService.TakeOwner(takes[0]).Index.Should().Be(2);           // the take starts at the first words

        StudioService.VoiceUnits(shots, null).Select(u => u[0].Index).Should().Equal(1, 5, 6, 7);
        StudioService.VoiceUnits(shots, shots[2].Id).Single().Select(s => s.Index).Should().Equal(1, 2, 3, 4); // "Đọc lại" redoes the scene
        shots[1].VoiceBlobId = Guid.NewGuid();
        StudioService.VoiceUnits(shots, null).Select(u => u[0].Index).Should().Equal(5, 6, 7);
    }

    [Fact]
    public void Styled_take_sends_the_direction_apart_from_the_words()
    {
        var body = NewsStudio.Infrastructure.Rendering.GeminiTtsService.BuildExpressiveRequest("gemini-3.8-flash-tts", "Sulafat", "Mẹ khẽ cười.", "tender, slow");
        body["model"]!.GetValue<string>().Should().Be("gemini-3.8-flash-tts");
        var content = body["input"]![0]!["content"]![0]!;
        content["text"]!.GetValue<string>().Should().Be("Mẹ khẽ cười.");                       // verbatim transcript only
        content["annotations"]![0]!["type"]!.GetValue<string>().Should().Be("speech_metadata");
        content["annotations"]![0]!["style"]!.GetValue<string>().Should().Be("tender, slow");
        body["generation_config"]!["speech_config"]![0]!["voice"]!.GetValue<string>().Should().Be("Sulafat");

        var pcm = Enumerable.Range(0, 9600).Select(i => (byte)(i % 251)).ToArray();
        using var wav = new MemoryStream();
        using (var w = new BinaryWriter(wav, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF"u8.ToArray()); w.Write(36 + pcm.Length); w.Write("WAVE"u8.ToArray());
            w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(24000); w.Write(48000); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8.ToArray()); w.Write(pcm.Length); w.Write(pcm);
        }
        var answer = JsonSerializer.Serialize(new { steps = new object[] { new { content = new object[] { new { type = "audio", mime_type = "audio/wav", data = Convert.ToBase64String(wav.ToArray()) } } } } });
        NewsStudio.Infrastructure.Rendering.GeminiTtsService.ParseExpressiveAudio(answer).Should().Equal(pcm);
        NewsStudio.Infrastructure.Rendering.GeminiTtsService.ParseExpressiveAudio("""{"steps":[]}""").Should().BeNull();
        StudioVoices.MaxPlausibleSeconds(string.Join(" ", Enumerable.Repeat("chữ", 36))).Should().Be(24);
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
    public async Task Compose_lets_a_scene_take_run_across_cuts()
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
            var clips = new[] { "a", "b", "c" }.Select(n => Path.Combine(dir, n + ".mp4")).ToArray();
            foreach (var c in clips) await Run($"-loglevel error -f lavfi -i testsrc=size=720x1280:rate=24 -t 2 -c:v libx264 -pix_fmt yuv420p \"{c}\"");
            var take = Path.Combine(dir, "take.m4a");  // one 3.5 s take on clip a, running into clip b
            var line = Path.Combine(dir, "line.m4a");  // the next voice, on clip c
            await Run($"-loglevel error -f lavfi -i sine=f=300:sample_rate=48000 -t 3.5 -c:a aac \"{take}\"");
            await Run($"-loglevel error -f lavfi -i sine=f=300:sample_rate=48000 -t 1 -c:a aac \"{line}\"");

            var media = new StudioMedia(new ConfigurationBuilder().Build());
            var output = Path.Combine(dir, "final.mp4");
            var seconds = await media.ComposeAsync(clips, null, 0.15, "9:16", "720p", output, CancellationToken.None, voices: new string?[] { take, null, line });

            seconds.Should().BeApproximately(6.0, 0.3);
            async Task<double> Mean(double from, double length)
            {
                var log = await Run($"-ss {from.ToString(System.Globalization.CultureInfo.InvariantCulture)} -t {length.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i \"{output}\" -af volumedetect -f null -");
                return double.Parse(System.Text.RegularExpressions.Regex.Match(log, @"mean_volume: (-?[\d.]+) dB").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            (await Mean(2.3, 1.0)).Should().BeGreaterThan(-40, "the take goes on over the cut into clip b");
            (await Mean(3.85, 0.25)).Should().BeLessThan(-60, "the take ends with its words, before clip c's voice (from 4.25 s)");
            (await Mean(4.4, 0.6)).Should().BeGreaterThan(-40);
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
