using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Infrastructure.Reup;
using NewsStudio.Infrastructure.Studio;
using Xunit;

namespace NewsStudio.Tests;

/// <summary>Reup Video: probing, output size, audio step, output checks, scenes, settings, stock search, licences.</summary>
public class ReupTests
{
    // ───────────── reading files ─────────────

    [Fact]
    public void Phone_video_filmed_upright_is_read_as_portrait_with_its_exact_frame_rate()
    {
        const string json = """
        {"streams":[{"codec_type":"video","codec_name":"h264","pix_fmt":"yuv420p","width":1920,"height":1080,"avg_frame_rate":"30000/1001","r_frame_rate":"30000/1001","duration":"12.012","nb_frames":"360","side_data_list":[{"rotation":-90}]},
                    {"codec_type":"audio","codec_name":"aac"}],"format":{"duration":"12.05"}}
        """;
        var p = ReupMedia.ParseProbe(json);
        p.Width.Should().Be(1080);
        p.Height.Should().Be(1920);
        p.FpsText.Should().Be("30000/1001");
        p.Fps.Should().BeApproximately(29.97, 0.01);
        p.Duration.Should().BeApproximately(12.012, 0.001);
        p.HasAudio.Should().BeTrue();
        p.Frames.Should().Be(360);
    }

    [Fact]
    public void Cover_art_is_not_taken_for_the_video_and_odd_frame_rates_fall_back()
    {
        const string json = """
        {"streams":[{"codec_type":"video","codec_name":"mjpeg","width":600,"height":600,"disposition":{"attached_pic":1}},{"codec_type":"audio"}],"format":{"duration":"200"}}
        """;
        var p = ReupMedia.ParseProbe(json);
        p.HasVideo.Should().BeFalse();
        p.HasAudio.Should().BeTrue();
        ReupMedia.PickFps("0/0", "25/1").Should().Be("25/1");
        ReupMedia.PickFps("90000/1", "1000/1").Should().Be("60/1");
    }

    [Theory]
    [InlineData(1080, 1920, 1080, 1080, 1920)]
    [InlineData(1080, 1920, 720, 720, 1280)]
    [InlineData(720, 1280, 1080, 720, 1280)] // never upscaled
    [InlineData(1920, 1080, 720, 1280, 720)]
    [InlineData(704, 1248, 1080, 704, 1248)]
    [InlineData(1081, 1921, 720, 720, 1280)] // even numbers for H.264
    public void Output_keeps_the_aspect_ratio_and_never_upscales(int w, int h, int shortSide, int ew, int eh) =>
        ReupMedia.OutputSize(w, h, shortSide).Should().Be((ew, eh));

    // ───────────── audio step ─────────────

    [Fact]
    public void Remove_mode_gives_a_silent_file_for_picking_music_on_facebook()
    {
        var args = ReupMedia.BuildMuxArgs("Remove", "v.mp4", "s.mp4", null, 12, 0, 1, 1, "o.mp4");
        args.Should().Contain("-an").And.Contain("copy");
        args.Should().NotContain("s.mp4");
    }

    [Theory]
    [InlineData("KeepOriginal")]
    [InlineData("Replace")]
    [InlineData("Mix")]
    public void Audio_modes_keep_the_video_length_and_copy_the_picture(string mode)
    {
        var args = ReupMedia.BuildMuxArgs(mode, "v.mp4", "s.mp4", "a.mp3", 12.5, 0, 0.8, 0.5, "o.mp4");
        var joined = string.Join(' ', args);
        joined.Should().Contain("-t 12.5").And.Contain("apad").And.Contain("-c:v copy").And.Contain("-c:a aac");
        if (mode == "Mix") joined.Should().Contain("amix=inputs=2").And.Contain("normalize=0");
        if (mode == "KeepOriginal") joined.Should().NotContain("a.mp3");
        if (mode == "Replace") joined.Should().NotContain("s.mp4");
    }

    [Fact]
    public void Audio_modes_without_their_file_are_refused()
    {
        var replace = () => ReupMedia.BuildMuxArgs("Replace", "v.mp4", "s.mp4", null, 5, 0, 1, 1, "o.mp4");
        replace.Should().Throw<ArgumentException>();
        var unknown = () => ReupMedia.BuildMuxArgs("Karaoke", "v.mp4", "s.mp4", null, 5, 0, 1, 1, "o.mp4");
        unknown.Should().Throw<ArgumentException>();
    }

    // ───────────── output checks ─────────────

    private static ReupProbe Good => new(12.0, "30/1", 30, 1080, 1920, true, false, "h264", "yuv420p", 360);
    private static ReupMedia.Expectation Expect => new(12.0, 30, 1080, 1920, false);

    [Fact]
    public void A_correct_output_passes_every_check() =>
        ReupMedia.Evaluate(Good, Expect, "").Should().OnlyContain(c => c.Ok);

    [Fact]
    public void Wrong_length_fps_size_audio_or_decode_errors_fail()
    {
        Fail(Good with { Duration = 11.5 }, "", "Thời lượng giữ nguyên");
        Fail(Good with { Fps = 25, FpsText = "25/1" }, "", "FPS giữ nguyên");
        Fail(Good with { Width = 720 }, "", "Kích thước");
        Fail(Good with { HasAudio = true }, "", "Âm thanh");
        Fail(Good with { VideoCodec = "hevc" }, "", "Chuẩn H.264");
        Fail(Good, "[h264 @ 0x1] error while decoding MB 3 4", "Giải mã trọn vẹn");
    }

    [Fact]
    public void Mostly_black_output_fails_but_a_short_black_moment_passes()
    {
        const string half = "[blackdetect @ 0x1] black_start:0 black_end:7 black_duration:7";
        Fail(Good, half, "Không bị đen hình");
        const string blink = "[blackdetect @ 0x1] black_start:0 black_end:1.2 black_duration:1.2";
        ReupMedia.Evaluate(Good, Expect, blink).Should().OnlyContain(c => c.Ok);
    }

    private static void Fail(ReupProbe p, string log, string check) =>
        ReupMedia.Evaluate(p, Expect, log).Single(c => c.Name == check).Ok.Should().BeFalse();

    // ───────────── scenes / placement / settings ─────────────

    [Fact]
    public void Scenes_start_at_zero_in_time_order_inside_the_video()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var result = ReupService.NormalizeSegments(new[]
        {
            new ReupSegmentDto { Start = 12, BackdropId = b },
            new ReupSegmentDto { Start = 2, BackdropId = a },
            new ReupSegmentDto { Start = 12.2, BackdropId = c },   // same moment as b: the later choice wins
            new ReupSegmentDto { Start = 40, BackdropId = a },     // past the end
            new ReupSegmentDto { Start = 5, BackdropId = Guid.Empty }
        }, 30);
        result.Select(s => s.Start).Should().Equal(0, 12.2);
        result.Select(s => s.BackdropId).Should().Equal(a, c);
    }

    [Fact]
    public void Placement_and_settings_are_kept_in_range()
    {
        var p = ReupService.NormalizePlacement(new ReupPlacementDto { Scale = 5, OffsetX = -3, Harmonize = 2, Blur = -1, Crossfade = 9 });
        p.Scale.Should().Be(1.3);
        p.OffsetX.Should().Be(-0.4);
        p.Harmonize.Should().Be(1);
        p.Blur.Should().Be(0);
        p.Crossfade.Should().Be(2);

        var s = ReupSettingsStore.Normalize(new ReupSettingsDto { Threads = 500, MaxRamMb = 10, ExecutionProvider = "CUDA", Preset = "placebo", Crf = 99, SwapperLicenseConfirmed = true });
        s.Threads.Should().Be(64);
        s.MaxRamMb.Should().Be(1024);
        s.ExecutionProvider.Should().Be("cpu");
        s.Preset.Should().Be("medium");
        s.Crf.Should().Be(28);
        s.SwapperLicenseConfirmed.Should().BeFalse(); // a licence confirmation needs the licence written down
    }

    // ───────────── stock search ─────────────

    [Fact]
    public void Pexels_and_pixabay_results_are_read_with_author_page_and_a_1080p_file()
    {
        const string pexelsVideos = """
        {"videos":[{"id":77,"url":"https://www.pexels.com/video/beach-77/","image":"https://images.pexels.com/videos/77/p.jpg","duration":14,"user":{"name":"Lan"},
          "video_files":[{"file_type":"video/mp4","width":2160,"height":3840,"link":"https://videos.pexels.com/4k.mp4"},
                         {"file_type":"video/mp4","width":1080,"height":1920,"link":"https://videos.pexels.com/hd.mp4"},
                         {"file_type":"video/mp4","width":360,"height":640,"link":"https://videos.pexels.com/sd.mp4"}]}]}
        """;
        var v = ReupStockClient.ParsePexelsVideos(pexelsVideos).Single();
        v.DownloadUrl.Should().Be("https://videos.pexels.com/hd.mp4");
        v.Author.Should().Be("Lan");
        v.PageUrl.Should().Be("https://www.pexels.com/video/beach-77/");
        v.DurationSeconds.Should().Be(14);

        const string pixabay = """{"hits":[{"id":5,"pageURL":"https://pixabay.com/photos/x-5/","webformatURL":"https://cdn.pixabay.com/w.jpg","largeImageURL":"https://cdn.pixabay.com/l.jpg","imageWidth":1080,"imageHeight":1920,"user":"Mai"}]}""";
        var p = ReupStockClient.ParsePixabayPhotos(pixabay).Single();
        p.DownloadUrl.Should().Be("https://cdn.pixabay.com/l.jpg");
        p.Author.Should().Be("Mai");
    }

    [Theory]
    [InlineData("Pexels", "https://videos.pexels.com/a.mp4", true)]
    [InlineData("Pexels", "https://images.pexels.com/a.jpg", true)]
    [InlineData("Pixabay", "https://cdn.pixabay.com/a.jpg", true)]
    [InlineData("Pexels", "https://pexels.com.evil.test/a.mp4", false)]
    [InlineData("Pixabay", "http://cdn.pixabay.com/a.jpg", false)]
    [InlineData("Pexels", "https://cdn.pixabay.com/a.jpg", false)]
    public void Stock_downloads_only_come_from_the_provider(string provider, string url, bool ok) =>
        ReupStockClient.IsProviderHost(provider, url).Should().Be(ok);

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.20.0.1", true)]
    [InlineData("192.168.1.10", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("104.18.20.1", false)]
    public void Links_to_private_addresses_are_refused(string ip, bool isPrivate) =>
        ReupStockClient.IsPrivate(IPAddress.Parse(ip)).Should().Be(isPrivate);
}

/// <summary>Reup Video through the real services and database (test DB) — and the whole PC pipeline when the engine is installed.</summary>
[Collection("DatabaseCollection")]
public class ReupFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ReupFlowTests(WebApplicationFactory<Program> factory) => _factory = factory;

    /// <summary>A small test clip made by FFmpeg (moving pattern + tone): no file needs to ship with the tests.</summary>
    private static async Task<string> MakeClipAsync(string dir, double seconds, bool audio, string size = "360x640")
    {
        var media = new ReupMedia(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var path = Path.Combine(dir, $"clip-{Guid.NewGuid():N}.mp4");
        var args = new List<string> { "-hide_banner", "-v", "error", "-y", "-f", "lavfi", "-i", $"testsrc2=size={size}:rate=25:duration={seconds}" };
        if (audio) args.AddRange(new[] { "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}" });
        args.AddRange(new[] { "-c:v", "libx264", "-pix_fmt", "yuv420p" });
        if (audio) args.AddRange(new[] { "-c:a", "aac" });
        args.Add(path);
        var (code, _, err) = await ReupMedia.RunAsync(media.Ffmpeg, args, CancellationToken.None);
        code.Should().Be(0, err);
        return path;
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "reup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task A_job_is_read_checked_queued_for_the_pc_only_and_cancelled()
    {
        var dir = NewDir();
        try
        {
            using var scope = _factory.Services.CreateScope();
            var reup = scope.ServiceProvider.GetRequiredService<ReupService>();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            var job = await reup.CreateJobAsync(await MakeClipAsync(dir, 3, audio: true), "Nhảy thử.mp4", null, CancellationToken.None);
            job.Title.Should().Be("Nhảy thử");
            job.SourceWidth.Should().Be(360);
            job.SourceHeight.Should().Be(640);
            job.SourceFps.Should().Be("25/1");
            job.SourceHasAudio.Should().BeTrue();
            job.SourceDurationSeconds.Should().BeApproximately(3, 0.1);
            job.HasSourceThumb.Should().BeTrue();
            job.OutputWidth.Should().Be(360); // never upscaled
            job.Status.Should().Be("Draft");

            // the owner's statements come first
            var start = () => reup.StartAsync(job.Id, false, CancellationToken.None);
            (await start.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*quyền sử dụng*");

            var input = new ReupJobInputDto { RightsConfirmed = true, PeopleConsentConfirmed = true, AudioMode = "KeepOriginal", OutputShortSide = 720 };
            await reup.UpdateJobAsync(job.Id, input, CancellationToken.None);
            // nothing to change on the picture: refused instead of re-encoding the same video
            (await start.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Chưa bật Thay cảnh nền*");
            var scene = await reup.AddBackdropAsync(await MakeClipAsync(dir, 1, false, "720x1280"), "s.mp4", new ReupBackdropInfoDto(), CancellationToken.None);
            input.BackgroundEnabled = true;
            input.Segments = new() { new() { Start = 0, BackdropId = scene.Id } };
            await reup.UpdateJobAsync(job.Id, input, CancellationToken.None);

            // face swap stays locked without a commercially licensed model
            input.FaceSwapEnabled = true;
            await reup.UpdateJobAsync(job.Id, input, CancellationToken.None);
            (await start.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*GIẤY PHÉP THƯƠNG MẠI*");
            input.FaceSwapEnabled = false;
            await reup.UpdateJobAsync(job.Id, input, CancellationToken.None);

            var queued = await reup.StartAsync(job.Id, false, CancellationToken.None);
            queued.Status.Should().Be("Queued");
            var row = await db.ReupJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
            var hangfire = JobStorage.Current.GetMonitoringApi().JobDetails(row.HangfireJobId);
            hangfire.History.First().Data["Queue"].Should().Be(ReupService.PcQueue); // Railway never takes it

            (await reup.CancelAsync(job.Id, CancellationToken.None)).Status.Should().Be("Cancelled");
            await reup.DeleteJobAsync(job.Id, CancellationToken.None);
            (await db.ReupJobs.AnyAsync(j => j.Id == job.Id)).Should().BeFalse();
            await reup.DeleteBackdropAsync(scene.Id, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>Big files go through the Vercel proxy in small pieces: start, pieces (one sent twice, like a retry), then the form.</summary>
    [Fact]
    public async Task A_video_sent_in_pieces_becomes_a_job()
    {
        var dir = NewDir();
        try
        {
            var bytes = await File.ReadAllBytesAsync(await MakeClipAsync(dir, 3, audio: true));
            var http = _factory.CreateClient();
            var start = await http.PostAsync("/api/reup/uploads", null);
            start.EnsureSuccessStatusCode();
            var uploadId = System.Text.Json.JsonDocument.Parse(await start.Content.ReadAsStringAsync()).RootElement.GetProperty("uploadId").GetString();
            const int piece = 64 * 1024;
            for (var offset = 0; offset < bytes.Length; offset += piece)
            {
                var part = new ByteArrayContent(bytes, offset, Math.Min(piece, bytes.Length - offset));
                (await http.PutAsync($"/api/reup/uploads/{uploadId}?offset={offset}", part)).EnsureSuccessStatusCode();
                if (offset == piece) // the same piece again (a retry after a network error) changes nothing
                    (await http.PutAsync($"/api/reup/uploads/{uploadId}?offset={offset}", new ByteArrayContent(bytes, offset, piece))).EnsureSuccessStatusCode();
            }
            var gap = await http.PutAsync($"/api/reup/uploads/{uploadId}?offset={bytes.Length + 10}", new ByteArrayContent(new byte[10]));
            gap.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var form = new MultipartFormDataContent
            {
                { new StringContent(uploadId!), "uploadId" },
                { new StringContent("Nhảy từng khúc.mp4"), "fileName" },
                { new StringContent("video/mp4"), "contentType" }
            };
            var created = await http.PostAsync("/api/reup/jobs", form);
            var body = await created.Content.ReadAsStringAsync();
            created.StatusCode.Should().Be(HttpStatusCode.OK, body);
            var job = System.Text.Json.JsonDocument.Parse(body).RootElement;
            job.GetProperty("title").GetString().Should().Be("Nhảy từng khúc");
            job.GetProperty("sourceSizeBytes").GetInt64().Should().Be(bytes.Length);
            job.GetProperty("sourceHasAudio").GetBoolean().Should().BeTrue();

            using var scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ReupService>().DeleteJobAsync(Guid.Parse(job.GetProperty("id").GetString()!), CancellationToken.None);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Backdrops_need_a_licence_and_characters_need_consent()
    {
        var dir = NewDir();
        try
        {
            using var scope = _factory.Services.CreateScope();
            var reup = scope.ServiceProvider.GetRequiredService<ReupService>();
            var clip = await MakeClipAsync(dir, 2, audio: false, size: "720x1280");

            var noLicence = () => reup.AddBackdropAsync(clip, "x.mp4", new ReupBackdropInfoDto { SourceName = "Pexels" }, CancellationToken.None);
            await noLicence.Should().ThrowAsync<ArgumentException>();

            var own = await reup.AddBackdropAsync(clip, "Biển chiều.mp4", new ReupBackdropInfoDto { Category = "beach" }, CancellationToken.None);
            own.Kind.Should().Be("Video");
            own.Category.Should().Be("Beach");
            own.Name.Should().Be("Biển chiều");
            own.LicenseName.Should().Contain("Của tôi");
            own.DurationSeconds.Should().BeApproximately(2, 0.1);
            (await reup.ListBackdropsAsync("Beach", null, "chiều", CancellationToken.None)).Should().Contain(b => b.Id == own.Id);
            await reup.DeleteBackdropAsync(own.Id, CancellationToken.None);

            var noConsent = () => reup.AddCharacterAsync("Lan", null, false, clip, "image/jpeg", CancellationToken.None);
            await noConsent.Should().ThrowAsync<ArgumentException>().WithMessage("*đồng ý*");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// The whole PC pipeline: source → engine (background replaced, scene change with cross-fade) → original audio →
    /// output checks → stored. Runs only where the engine is installed (scripts/pc/setup-reup-engine.bat).
    /// </summary>
    [Fact]
    public async Task The_pc_pipeline_replaces_the_background_and_keeps_length_fps_and_audio()
    {
        using var scope = _factory.Services.CreateScope();
        var paths = scope.ServiceProvider.GetRequiredService<ReupPaths>();
        if (!paths.Installed) return; // not this machine
        var reup = scope.ServiceProvider.GetRequiredService<ReupService>();
        var files = scope.ServiceProvider.GetRequiredService<StudioFileStore>();
        var dir = NewDir();
        try
        {
            var job = await reup.CreateJobAsync(await MakeClipAsync(dir, 3, audio: true), "source.mp4", "Pipeline", CancellationToken.None);
            var sceneA = await reup.AddBackdropAsync(await MakeClipAsync(dir, 1, false, "720x1280"), "a.mp4", new ReupBackdropInfoDto(), CancellationToken.None);
            var sceneB = await reup.AddBackdropAsync(await MakeClipAsync(dir, 1, false, "720x1280"), "b.mp4", new ReupBackdropInfoDto(), CancellationToken.None);
            await reup.UpdateJobAsync(job.Id, new ReupJobInputDto
            {
                RightsConfirmed = true, PeopleConsentConfirmed = true, BackgroundEnabled = true, AudioMode = "KeepOriginal", OutputShortSide = 720,
                Segments = new() { new() { Start = 0, BackdropId = sceneA.Id }, new() { Start = 1.5, BackdropId = sceneB.Id } }
            }, CancellationToken.None);
            await reup.StartAsync(job.Id, false, CancellationToken.None);

            var watch = Stopwatch.StartNew();
            await reup.RunJobAsync(job.Id, false, CancellationToken.None);
            var done = await reup.GetJobAsync(job.Id, CancellationToken.None);
            done.ErrorMessage.Should().BeNull();
            done.Status.Should().Be("Done");
            done.Qc.Should().NotBeEmpty().And.OnlyContain(c => c.Ok);
            done.OutputWidth.Should().Be(360);
            done.OutputDurationSeconds.Should().BeApproximately(3, 0.1);
            done.FramesDone.Should().BeGreaterThan(60);
            done.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(3));

            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var row = await db.ReupJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
            if (row.HangfireJobId != null) BackgroundJob.Delete(row.HangfireJobId); // the test ran it directly
            (await files.ExistsAsync(row.OutputBlobId!.Value, CancellationToken.None)).Should().BeTrue();

            await reup.DeleteJobAsync(job.Id, CancellationToken.None);
            await reup.DeleteBackdropAsync(sceneA.Id, CancellationToken.None);
            await reup.DeleteBackdropAsync(sceneB.Id, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
