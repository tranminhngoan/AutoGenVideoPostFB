using System.Text.Json;
using FluentAssertions;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Studio;
using Xunit;

namespace NewsStudio.Tests;

/// <summary>Omni Studio: Omni request/response handling, shot planning and prompts (no network).</summary>
public class StudioTests
{
    [Fact]
    public void Omni_request_puts_reference_images_before_the_prompt_and_clamps_duration()
    {
        var body = GeminiOmniClient.BuildRequest("gemini-omni-1.2-flash", "A man calls out", new[]
        {
            new OmniReference(new byte[] { 1, 2, 3 }, "image/jpeg"),
            new OmniReference(new byte[] { 4 }, "image/jpeg"),
        }, "9:16", "720p", 30);
        var json = JsonSerializer.Serialize(body);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;

        r.GetProperty("model").GetString().Should().Be("gemini-omni-1.2-flash");
        var input = r.GetProperty("input").EnumerateArray().ToList();
        input.Should().HaveCount(3);
        input[0].GetProperty("type").GetString().Should().Be("image");
        input[0].GetProperty("data").GetString().Should().Be("AQID");
        input[2].GetProperty("type").GetString().Should().Be("text");
        var fmt = r.GetProperty("response_format");
        fmt.GetProperty("type").GetString().Should().Be("video");
        fmt.GetProperty("aspect_ratio").GetString().Should().Be("9:16");
        fmt.GetProperty("duration").GetString().Should().Be("15s"); // Omni max
    }

    [Theory]
    [InlineData("""{"id":"int-1","steps":[{"content":[{"type":"text","text":"ok"},{"type":"video","mime_type":"video/mp4","data":"AAEC"}]}]}""", "AAEC", null, "int-1")]
    [InlineData("""{"name":"interactions/abc","output_video":{"mime_type":"video/mp4","data":"Zm9v"}}""", "Zm9v", null, "interactions/abc")]
    [InlineData("""{"id":"x","outputs":[{"type":"video","uri":"https://generativelanguage.googleapis.com/v1beta/files/v1:download"}]}""", null, "https://generativelanguage.googleapis.com/v1beta/files/v1:download", "x")]
    public void Omni_response_video_is_found_whatever_the_nesting(string json, string? data, string? uri, string id)
    {
        var p = GeminiOmniClient.ParseVideo(json);
        p.Data.Should().Be(data);
        p.Uri.Should().Be(uri);
        p.InteractionId.Should().Be(id);
    }

    [Fact]
    public void Omni_response_without_video_gives_nothing()
    {
        var p = GeminiOmniClient.ParseVideo("""{"id":"x","steps":[{"content":[{"type":"text","text":"blocked"}]}]}""");
        p.Data.Should().BeNull();
        p.Uri.Should().BeNull();
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
        plan.Shots[1].Duration.Should().Be(15); // clamped to Omni's max
        plan.Shots[1].Speaker.Should().BeNull(); // "null" string treated as none
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
            var a = Path.Combine(dir, "a.mp4");   // Omni-like clip with speech
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
        }
        finally
        {
            StudioMedia.TryDelete(dir);
        }
    }

    [Theory]
    [InlineData("9:16", "720p", 720, 1280)]
    [InlineData("9:16", "1080p", 1080, 1920)]
    [InlineData("16:9", "720p", 1280, 720)]
    public void Frame_size_follows_aspect_and_resolution(string aspect, string res, int w, int h) =>
        StudioMedia.FrameSize(aspect, res).Should().Be((w, h));
}
