using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Infrastructure.AiStudio;
using NewsStudio.Infrastructure.Studio;
using Xunit;

namespace NewsStudio.Tests;

/// <summary>AI Video Studio: prompt engine, upload checks, probing, request bodies, settings (no database, no network).</summary>
public class AiStudioUnitTests
{
    private static JsonObject Analysis() => JsonNode.Parse("""
    {
      "summary": "A young woman walks along a garden path and turns toward the camera",
      "summary_vi": "Cô gái đi dọc lối vườn rồi quay về phía máy quay",
      "characters": [{"label":"woman","appearance":"young woman, slim build","hair":"long black hair with a black bow","clothing":"pink ribbed camisole and shorts","accessories":"none","position":"center","posture":"upright","gaze":"down"}],
      "timeline": [
        {"start": 10.0, "end": 12.0, "action": "walks forward slowly", "body": "right foot steps, arms swing gently", "confidence": "high"},
        {"start": 12.0, "end": 14.0, "action": "turns her torso to the left", "body": "weight shifts to the left foot", "confidence": "medium"},
        {"start": 14.0, "end": 16.0, "action": "uncertain", "body": "uncertain", "confidence": "low"}
      ],
      "body_movement": {"head":"slight tilt","shoulders":"relaxed","arms":"swing","hands":"open","torso":"upright","hips":"sway","legs":"steady steps","feet":"barefoot","coordination":"smooth relaxed walk"},
      "facial_expression": {"expression":"soft smile","emotion":"calm","eyes":"looking down","gaze_direction":"down","changes":"uncertain"},
      "environment": {"location":"garden path","space":"outdoor","landscape":"green shrubs and pine trees","architecture":"red torii gate","background":"pine trees","foreground":"tall grass","weather":"clear","time_of_day":"daytime","objects":["shrubs","gate","uncertain"]},
      "object_interaction": "touches a shrub",
      "camera": {"angle":"eye-level","shot_size":"medium shot","movement_seen":"moving backward"},
      "composition": {"subject_placement":"center","rule_of_thirds":"uncertain","leading_lines":"path","foreground":"grass","background":"trees","negative_space":"sky","depth":"layered"},
      "lighting": {"source":"natural daylight","direction":"front","intensity":"soft","shadows":"soft","highlights":"skin","color_temperature":"neutral"},
      "color": {"dominant":"green and pink","tone":"neutral","saturation":"medium","contrast":"medium","white_balance":"neutral","grading":"natural"},
      "depth_of_field": {"focus_subject":"woman","background_blur":"slightly blurred","foreground_blur":"uncertain","focus_change":"uncertain","lens_look":"uncertain"},
      "motion_quality": {"naturalness":"natural","acceleration":"gentle","inertia":"hair sways","balance":"stable","cloth":"fabric ripples","hair":"swings","environment":"leaves move"},
      "realism": {"skin":"natural texture","hair":"fine strands","fabric":"ribbed knit","surfaces":"uncertain","reflections":"uncertain","shadows":"soft","physics":"realistic","overall":"photorealistic"},
      "confidence": 0.85
    }
    """)!.AsObject();

    private static ScenePromptInput Input(string mode = "Faithful", AiStudioVariationDto? v = null, string camera = "pull-out (zoom out / dolly out)",
        bool continuation = false, int target = 6, string global = "") => new(
        3, 10.0, 16.0, target, continuation, true, Analysis(),
        JsonNode.Parse($$"""{"camera_movement":["{{camera}}"],"motion_level":"medium","tone":"neutral"}""")!.AsObject(),
        JsonNode.Parse("""[{"id":"C1","label":"woman","appearance":"young woman, slim build","hair":"long black hair with a black bow","clothing":"pink ribbed camisole and shorts","accessories":"none","scenes":[3]}]""")!.AsArray(),
        JsonNode.Parse("""[{"id":"E1","location":"Japanese-style garden","background":"pine trees and a red torii gate","scenes":[3]}]""")!.AsArray(),
        new JsonObject(), mode, v ?? new AiStudioVariationDto(), global, "");

    [Fact]
    public void Faithful_prompt_follows_the_analysis_in_order_without_uncertain_facts()
    {
        var prompt = AiStudioPromptEngine.Build(Input());

        prompt.Should().Contain("Subject: young woman, slim build, long black hair with a black bow, wearing pink ribbed camisole and shorts");
        prompt.Should().NotContain("with none");
        prompt.Should().Contain("Setting: Japanese-style garden");
        prompt.Should().Contain("Action: A young woman walks along a garden path");
        prompt.Should().Contain("0–2s walks forward slowly, right foot steps, arms swing gently; 2–4s turns her torso to the left");
        prompt.Should().Contain("Camera: medium shot, eye-level angle, pull-out");
        prompt.Should().Contain("Lighting: natural daylight, from front");
        prompt.Should().Contain("Depth of field: focus on woman");
        prompt.Should().NotContainEquivalentOf("uncertain");
        prompt.Should().EndWith($"Avoid: {AiStudioPromptEngine.DefaultNegative}.");
        // sections in the order of the brief: subject → setting → action → sequence → … → quality → constraints
        var order = new[] { "Subject:", "Setting:", "Action:", "Sequence:", "Body mechanics:", "Expression:", "Camera:", "Composition:", "Lighting:", "Color:", "Depth of field:", "Realism:", "Continuity:", "Quality:", "Avoid:" };
        order.Select(o => prompt.IndexOf(o, StringComparison.Ordinal)).Should().BeInAscendingOrder().And.NotContain(-1);
    }

    [Fact]
    public void The_timed_sequence_is_mapped_onto_the_clip_length()
    {
        // a 6 s scene made as a 3 s clip: 0–2 s becomes 0–1 s
        AiStudioPromptEngine.Sequence(Input(target: 3)).Should().StartWith("0–1s walks forward slowly");
        AiStudioPromptEngine.Sequence(Input(target: 12)).Should().StartWith("0–4s walks forward slowly");
    }

    [Fact]
    public void Creative_mode_changes_only_what_the_user_chose()
    {
        AiStudioPromptEngine.HasVariation(new AiStudioVariationDto()).Should().BeFalse();
        var v = new AiStudioVariationDto { Character = "a middle-aged man with short grey hair", Setting = "a snowy mountain trail", Lighting = "golden hour backlight" };
        var prompt = AiStudioPromptEngine.Build(Input("Creative", v));
        prompt.Should().Contain("Subject: a middle-aged man with short grey hair");
        prompt.Should().Contain("Setting: a snowy mountain trail");
        prompt.Should().Contain("Lighting: golden hour backlight");
        prompt.Should().NotContain("pink ribbed camisole");
        prompt.Should().NotContain("Japanese-style garden");
        prompt.Should().Contain("walks forward slowly"); // the actions stay
    }

    [Fact]
    public void Cinematic_mode_upgrades_camera_light_grade_and_depth()
    {
        var prompt = AiStudioPromptEngine.Build(Input("Cinematic", camera: "handheld (small natural shake)"));
        prompt.Should().Contain("smooth stabilized gimbal move");
        prompt.Should().Contain("soft motivated key light");
        prompt.Should().Contain("cinematic color grade");
        prompt.Should().Contain("shallow depth of field");
        AiStudioPromptEngine.Build(Input("Cinematic", camera: "static (locked-off)")).Should().Contain("locked-off tripod shot");
    }

    [Fact]
    public void A_split_shot_continues_and_the_global_prompt_leads()
    {
        var prompt = AiStudioPromptEngine.Build(Input(continuation: true, global: "Warm summer film look"));
        prompt.Should().StartWith("Warm summer film look.");
        prompt.Should().Contain("continues the previous shot without a cut");
        AiStudioPromptEngine.Build(Input()).Length.Should().BeLessThanOrEqualTo(AiStudioPromptEngine.MaxPromptChars + 400);
    }

    [Fact]
    public void Repeated_phrases_are_said_once_and_the_global_prompt_has_no_scene_specific_clauses()
    {
        var input = Input();
        input.Analysis["environment"]!["location"] = "dirt ground, bamboo wall";
        input.Analysis["environment"]!["landscape"] = "Dirt ground, open yard";
        input.Environments.Clear();
        var setting = AiStudioPromptEngine.Build(input).Split("Setting: ")[1].Split('.')[0];
        setting.Should().Be("dirt ground, bamboo wall, open yard, red torii gate, background: pine trees, foreground: tall grass, objects: shrubs, gate, clear, daytime");

        var style = JsonNode.Parse("""{"visual_style":"Naturalistic documentary look, warm tones in Scene 1, cool tones in Scene 2.","realism_level":"photorealistic","color_palette":"warm"}""")!.AsObject();
        AiStudioPromptEngine.DefaultGlobalPrompt(style).Should().Be("Naturalistic documentary look; realism: photorealistic.");
    }

    [Fact]
    public void The_subject_says_who_the_person_looks_like_and_every_garment()
    {
        var input = Input();
        input.Characters.Clear();
        input.Characters.Add(JsonNode.Parse("""
            {"id":"C1","label":"boy","gender":"male","age_group":"about 6 years old","apparent_ethnicity":"Southeast Asian","skin_tone":"light-brown",
             "build":"slim","hair":"short straight black","clothing":"royal-blue cable-knit wool sweater, dark-brown trousers rolled to mid-calf",
             "footwear":"barefoot","accessories":"none","scenes":[3]}
            """));
        AiStudioPromptEngine.Build(input).Should().Contain(
            "Subject: Southeast Asian boy about 6 years old, light-brown skin, slim, short straight black hair, wearing royal-blue cable-knit wool sweater, dark-brown trousers rolled to mid-calf, barefoot.");
        // the creative mode's new character replaces all of it
        AiStudioPromptEngine.Build(input with { Mode = "Creative", Variation = new AiStudioVariationDto { Character = "an elderly woman" } })
            .Should().Contain("Subject: an elderly woman.").And.NotContain("Southeast Asian");
    }

    [Fact]
    public void The_channel_theme_decides_who_the_people_are_and_the_look()
    {
        // the reference showed a pale woman: the "Asian beauty" theme makes her East Asian with fair skin, bright and tasteful
        var beauty = AiStudioPromptEngine.Build(Input() with { Topic = AiStudioTopics.AsianBeauty });
        beauty.Should().Contain("Subject: East Asian (Korean, Japanese or Vietnamese look) woman, fair porcelain skin with a natural healthy glow, young woman, slim build, long black hair");
        beauty.Should().Contain("Look: beautiful young East Asian appearance");
        beauty.Should().Contain("K-drama style look");
        beauty.Should().Contain("Western or European facial features").And.Contain("vulgar or sexualized framing");

        var child = Input() with { Topic = AiStudioTopics.VietnamChildhood };
        child.Characters.Clear();
        child.Characters.Add(JsonNode.Parse("""{"id":"C1","label":"boy","age_group":"about 6 years old","apparent_ethnicity":"White","skin_tone":"pale","clothing":"blue sweater","scenes":[3]}"""));
        var memory = AiStudioPromptEngine.Build(child);
        memory.Should().Contain("Subject: Vietnamese (Southeast Asian) boy about 6 years old, sun-tanned brown skin from working outdoors, wearing blue sweater");
        memory.Should().NotContain("White").And.NotContain("pale skin");
        memory.Should().Contain("poor rural Vietnamese village in the 1990s");
        memory.Should().Contain("nostalgic warm golden afternoon light");
        memory.Should().Contain("smartphones");

        AiStudioTopics.Normalize("VN-CHILDHOOD").Should().Be(AiStudioTopics.VietnamChildhood);
        AiStudioTopics.Normalize("anything").Should().Be(AiStudioTopics.None);
        AiStudioPromptEngine.Build(Input()).Should().NotContain("Look:"); // no theme = only what the video showed
    }

    [Fact]
    public void Each_clip_gets_the_original_sound_of_its_own_scene_fitted_to_its_length()
    {
        var f = AiStudioMedia.BuildSoundFilter(new[]
        {
            new AiStudioMedia.SoundPiece(0, 4.8, 5.0),   // 4 % shorter → slowed a little
            new AiStudioMedia.SoundPiece(10, 22, 6.0),   // twice as long → at most 25 % faster, the rest cut
            new AiStudioMedia.SoundPiece(4.8, 10, 5.2),  // same length → untouched
        });
        f.Should().Contain("[1:a:0]atrim=start=0:end=4.8,asetpts=PTS-STARTPTS,atempo=0.96,apad=whole_dur=5,atrim=0:5,asetpts=PTS-STARTPTS[a0];");
        f.Should().Contain("atrim=start=10:end=22,asetpts=PTS-STARTPTS,atempo=1.25,apad=whole_dur=6,atrim=0:6");
        f.Should().Contain("atrim=start=4.8:end=10,asetpts=PTS-STARTPTS,apad=whole_dur=5.2");
        f.Should().EndWith("[a0][a1][a2]concat=n=3:v=0:a=1[aout]");
    }

    [Fact]
    public void Each_scene_is_narrated_by_an_ai_voice_laid_under_its_own_clip()
    {
        var f = AiStudioMedia.BuildVoiceFilter(new[]
        {
            new AiStudioMedia.VoicePiece("a.m4a", 3.2, 5.0),   // shorter than the clip → silence after it
            new AiStudioMedia.VoicePiece(null, 0, 4.0),        // no narration in this scene → silent
            new AiStudioMedia.VoicePiece("b.m4a", 7.0, 6.0),   // a little longer → played faster, then cut
            new AiStudioMedia.VoicePiece("c.m4a", 12.0, 6.0),  // far longer → at most 25 % faster
        });
        f.Should().Contain("[1:a:0]aformat=sample_rates=48000:channel_layouts=stereo,asetpts=PTS-STARTPTS,apad=whole_dur=5,atrim=0:5,asetpts=PTS-STARTPTS[a0];");
        f.Should().Contain("anullsrc=r=48000:cl=stereo,atrim=0:4,asetpts=PTS-STARTPTS[a1];");
        f.Should().Contain("[2:a:0]aformat=sample_rates=48000:channel_layouts=stereo,asetpts=PTS-STARTPTS,atempo=1.1765,apad=whole_dur=6");
        f.Should().Contain("[3:a:0]aformat=sample_rates=48000:channel_layouts=stereo,asetpts=PTS-STARTPTS,atempo=1.25,");
        f.Should().EndWith("[a0][a1][a2][a3]concat=n=4:v=0:a=1[aout]");
    }

    [Fact]
    public void Narration_goes_to_one_scene_each_and_into_its_prompt_as_story_context()
    {
        var lines = AiStudioNarration.Lines("""
        {"language":"vi","segments":[
          {"start":0.2,"end":1.6,"text":"Hồi đó nhà tôi nghèo lắm.","en":"Back then my family was very poor."},
          {"start":1.5,"end":2.6,"text":"Câu này vắt qua chỗ cắt.","en":"This line runs across the cut."},
          {"start":2.8,"end":3.8,"text":"Mẹ đi chợ từ sáng sớm."}],
         "voice":{"pitch_hz":112.4,"syllables_per_second":3.1,"loudness_std_db":5.2}}
        """);
        lines.Should().HaveCount(3);
        var parts = AiStudioNarration.Assign(lines, new[] { (0.0, 2.0), (2.0, 4.0) });
        parts[0].Text.Should().Be("Hồi đó nhà tôi nghèo lắm.");
        parts[0].En.Should().Be("Back then my family was very poor.");
        parts[1].Text.Should().Be("Câu này vắt qua chỗ cắt. Mẹ đi chợ từ sáng sớm."); // middle at 2.05 s → the second scene, once
        parts[1].En.Should().BeNull();                                                 // a line without meaning → the prompt quotes the words
        AiStudioNarration.PromptText(parts[0].Text, parts[0].En, edited: false).Should().Be("Back then my family was very poor.");
        AiStudioNarration.PromptText(parts[0].Text, parts[0].En, edited: true).Should().Be("Hồi đó nhà tôi nghèo lắm.");
        AiStudioNarration.PromptText(null, "x", false).Should().BeNull();

        var prompt = AiStudioPromptEngine.Build(Input() with { Narration = "Back then my \"family\" was very poor." });
        prompt.Should().Contain("Narration over this scene (off-screen voice-over, for the story only — nobody lip-syncs it, no text on screen): \"Back then my 'family' was very poor\".");
        prompt.IndexOf("Narration over", StringComparison.Ordinal).Should().BeGreaterThan(prompt.IndexOf("Sequence:", StringComparison.Ordinal));
        AiStudioPromptEngine.Build(Input()).Should().NotContain("Narration over");
        AiStudioPromptEngine.NarrationLine(new string('a', 900))!.Should().Contain(new string('a', AiStudioPromptEngine.MaxNarrationChars) + "…");
    }

    [Fact]
    public void A_long_spoken_stretch_is_split_between_scenes_where_each_word_was_said()
    {
        var scenes = new[] { (0.0, 6.0), (6.0, 12.0), (12.0, 18.0), (18.0, 20.0) };
        string Words(int from, int count) => string.Join(" ", Enumerable.Range(from, count).Select(i => $"w{i}"));

        // an older transcript: one 18 s stretch without word times → spread evenly over the three scenes it covers
        var old = AiStudioNarration.Assign(new[] { new AiStudioNarration.Line(0, 18, Words(0, 36), "One long stretch.") }, scenes);
        old[0].Text.Should().Be(Words(0, 12));
        old[1].Text.Should().Be(Words(12, 12));
        old[2].Text.Should().Be(Words(24, 12));
        old[3].Text.Should().BeNull();
        old[0].En.Should().Be("One long stretch."); // the meaning goes with every part

        // word times: each word to the scene where it was said; a short sentence across a cut stays whole
        var timed = new AiStudioNarration.Line(1, 13, "a b c d", null, new[]
        {
            new AiStudioNarration.Word(1, 4, "a"), new AiStudioNarration.Word(4, 5.5, "b"),
            new AiStudioNarration.Word(6.5, 9, "c"), new AiStudioNarration.Word(9, 13, "d")
        });
        var shortLine = new AiStudioNarration.Line(17, 19.5, "Câu ngắn qua cắt.", null);
        var parts = AiStudioNarration.Assign(new[] { timed, shortLine }, scenes);
        parts[0].Text.Should().Be("a b");
        parts[1].Text.Should().Be("c d");
        parts[3].Text.Should().Be("Câu ngắn qua cắt."); // middle 18.25 s → the last scene, read in one piece
        parts[2].Text.Should().BeNull();

        // little speech: sentences are never cut
        var whole = AiStudioNarration.Assign(new[] { new AiStudioNarration.Line(4, 9, "Một câu vừa phải.", null) }, scenes);
        whole[1].Text.Should().Be("Một câu vừa phải.");
        whole[0].Text.Should().BeNull();

        // the transcript keeps word times from the analyzer
        var lines = AiStudioNarration.Lines("""{"segments":[{"start":1,"end":2,"text":"Hồi đó.","words":[[1,1.4,"Hồi"],[1.4,2,"đó."]]}]}""");
        lines.Single().Words!.Select(w => w.Text).Should().Equal("Hồi", "đó.");
    }

    [Fact]
    public void The_narrator_sounds_like_the_reference_speaker_within_the_theme()
    {
        var deepSlowMan = new AiStudioNarration.Speaker(108, 3.0, 5);
        var (voice, style) = AiStudioNarration.Suggest(deepSlowMan, AiStudioTopics.VietnamChildhood);
        voice.Should().Be("gemini:Algenib");
        style.Should().Contain("deep, slightly husky male storyteller").And.Contain("slowly and calmly").And.Contain("poor childhood in the Vietnamese countryside");
        AiStudioNarration.Describe(deepSlowMan).Should().Be("Giọng nam rất trầm (~108 Hz), đọc chậm (3.0 tiếng/giây), lên xuống vừa phải");

        var woman = new AiStudioNarration.Speaker(232, 5.1, 8.5);
        var (wv, ws) = AiStudioNarration.Suggest(woman, AiStudioTopics.AsianBeauty);
        StudioVoices.All.Single(v => v.Id == wv).Gender.Should().Be("female");
        ws.Should().Contain("briskly").And.Contain("expressive").And.Contain("soft, sweet");
        AiStudioNarration.GenderOf(woman).Should().Be("female");

        // nothing measured: the theme decides (the childhood channel's storyteller is a man)
        AiStudioNarration.Suggest(null, AiStudioTopics.VietnamChildhood).Voice.Should().Be("gemini:Charon");
        AiStudioNarration.SpeakerOf("""{"segments":[],"voice":{"pitch_hz":150,"syllables_per_second":4}}""")!.PitchHz.Should().Be(150);
        AiStudioNarration.SpeakerOf("""{"segments":[]}""").Should().BeNull();
        AiStudioNarration.Key("Câu một", "gemini:Charon", "slow").Should().NotBe(AiStudioNarration.Key("Câu một", "gemini:Charon", "fast"));
    }

    [Fact]
    public void Names_and_handles_never_reach_the_prompt()
    {
        var input = Input();
        input.Analysis["summary"] = "@dancer_vn named Linh Tran dances";
        var prompt = AiStudioPromptEngine.Build(input);
        prompt.Should().NotContain("@dancer_vn").And.NotContain("Linh Tran");
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m' }, "mp4")]
    [InlineData(new byte[] { 0, 0, 0, 0x14, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'q', (byte)'t', (byte)' ', (byte)' ' }, "mov")]
    [InlineData(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x01, 0, 0, 0, 0, 0, 0, 0 }, "webm")]
    [InlineData(new byte[] { (byte)'M', (byte)'Z', 0x90, 0, 3, 0, 0, 0, 4, 0, 0, 0 }, null)]
    [InlineData(new byte[] { (byte)'<', (byte)'h', (byte)'t', (byte)'m', (byte)'l', (byte)'>', 0, 0, 0, 0, 0, 0 }, null)]
    public void The_real_container_is_read_from_the_first_bytes(byte[] head, string? expected) =>
        AiStudioMedia.SniffContainer(head).Should().Be(expected);

    [Fact]
    public void Probe_reads_rotation_bitrate_and_audio_tracks()
    {
        const string json = """
        {"streams":[{"codec_type":"video","codec_name":"hevc","width":1920,"height":1080,"avg_frame_rate":"30000/1001","r_frame_rate":"30000/1001","duration":"12.0","side_data_list":[{"rotation":-90}]},
                    {"codec_type":"audio","codec_name":"aac","sample_rate":"44100","channels":2}],
         "format":{"duration":"12.05","bit_rate":"8123456","format_name":"mov,mp4,m4a,3gp,3g2,mj2"}}
        """;
        var p = AiStudioMedia.ParseProbe(json);
        (p.Width, p.Height).Should().Be((1080, 1920));
        p.VideoCodec.Should().Be("hevc");
        p.Fps.Should().BeApproximately(29.97, 0.01);
        p.BitrateKbps.Should().Be(8123);
        p.Audio.Should().ContainSingle().Which.SampleRate.Should().Be(44100);
        p.Audio[0].Channels.Should().Be(2);
        AiStudioMedia.AspectOf(p.Width, p.Height).Should().Be("9:16");
        AiStudioMedia.GenerationAspect(1920, 1080).Should().Be("16:9");
        AiStudioMedia.AspectOf(1080, 1350).Should().Be("4:5");
    }

    [Fact]
    public void Seedance_request_is_unchanged_without_an_image_and_carries_it_inline_with_one()
    {
        // regression: the video studio page's requests stay byte-for-byte the same
        SeedanceVideoClient.BuildStartBody("p", "9:16", "720p", 5, 7).ToJsonString().Should().Be(
            """{"prompt":"p","n":1,"aspectRatio":"9:16","duration":5,"providerOptions":{"bytedance":{"watermark":false,"cameraFixed":false}},"resolution":"720p","seed":7}""");
        var body = SeedanceVideoClient.BuildStartBody("p", "9:16", "720p", 5, null, new VideoStartImage(new byte[] { 1, 2, 3 }, "image/jpeg"));
        body["image"]!["type"]!.GetValue<string>().Should().Be("file");
        body["image"]!["mediaType"]!.GetValue<string>().Should().Be("image/jpeg");
        body["image"]!["data"]!.GetValue<string>().Should().Be("AQID");
    }

    [Fact]
    public void Settings_are_kept_in_range()
    {
        var s = AiStudioSettingsStore.Normalize(new AiStudioSettingsDto { DefaultModel = "70b", Gpu = "cuda", MaxFramesPerScene = 50, Threads = -3, SourceRetentionDays = 0, ClipRetentionDays = 999 });
        s.DefaultModel.Should().Be("4b");
        s.Gpu.Should().Be("auto");
        s.MaxFramesPerScene.Should().Be(10);
        s.Threads.Should().Be(0);
        s.SourceRetentionDays.Should().Be(7);
        s.ClipRetentionDays.Should().Be(90);
        AiStudioSettingsStore.NormalizeModel("8B").Should().Be("8b");
    }
}

/// <summary>AI Video Studio end to end against the test database: upload, PC queue, analysis results, prompts, Seedance (fake gateway).</summary>
public class AiStudioFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AiStudioFlowTests(WebApplicationFactory<Program> factory) => _factory = factory;

    internal static async Task<string> MakeClipAsync(string dir, double seconds, bool audio, string size = "360x640")
    {
        var media = new AiStudioMedia(new ConfigurationBuilder().Build());
        var path = Path.Combine(dir, $"clip-{Guid.NewGuid():N}.mp4");
        var args = new List<string> { "-hide_banner", "-v", "error", "-y", "-f", "lavfi", "-i", $"testsrc2=size={size}:rate=24:duration={seconds}" };
        if (audio) args.AddRange(new[] { "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}" });
        args.AddRange(new[] { "-c:v", "libx264", "-pix_fmt", "yuv420p" });
        if (audio) args.AddRange(new[] { "-c:a", "aac" });
        args.Add(path);
        var (code, _, err) = await AiStudioMedia.RunAsync(media.Ffmpeg, args, CancellationToken.None);
        code.Should().Be(0, err);
        return path;
    }

    internal static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ai-studio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task A_reference_video_is_checked_stored_and_queued_for_the_pc_only()
    {
        var dir = NewDir();
        try
        {
            using var scope = _factory.Services.CreateScope();
            var studio = scope.ServiceProvider.GetRequiredService<AiStudioService>();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            // a renamed text file and a too-short clip are refused before anything is stored
            var fake = Path.Combine(dir, "fake.mp4");
            await File.WriteAllTextAsync(fake, "<html>not a video</html>");
            var bad = () => studio.CreateProjectAsync(fake, "fake.mp4", null, CancellationToken.None);
            (await bad.Should().ThrowAsync<ArgumentException>()).WithMessage("*MP4 / MOV / WEBM*");
            var tiny = await MakeClipAsync(dir, 1, false);
            var shortClip = () => studio.CreateProjectAsync(tiny, "x.mp4", null, CancellationToken.None);
            (await shortClip.Should().ThrowAsync<ArgumentException>()).WithMessage("*quá ngắn*");

            var p = await studio.CreateProjectAsync(await MakeClipAsync(dir, 4, audio: true), "Nhảy vườn.mp4", null, CancellationToken.None);
            p.Title.Should().Be("Nhảy vườn");
            (p.Width, p.Height).Should().Be((360, 640));
            p.FpsText.Should().Be("24/1");
            p.AspectRatio.Should().Be("9:16");
            p.GenAspectRatio.Should().Be("9:16");
            p.VideoCodec.Should().Be("h264");
            p.HasAudio.Should().BeTrue();
            p.AudioTracks.Should().ContainSingle();
            p.DurationSeconds.Should().BeApproximately(4, 0.1);
            p.HasThumb.Should().BeTrue();
            p.Status.Should().Be("Draft");
            p.NegativePrompt.Should().Be(AiStudioPromptEngine.DefaultNegative);

            var queued = await studio.StartAnalysisAsync(p.Id, false, CancellationToken.None);
            queued.Status.Should().Be("Queued");
            var row = await db.AiStudioProjects.AsNoTracking().SingleAsync(x => x.Id == p.Id);
            JobStorage.Current.GetMonitoringApi().JobDetails(row.HangfireJobId).History.First().Data["Queue"].Should().Be(AiStudioService.PcQueue);

            (await studio.CancelAnalysisAsync(p.Id, CancellationToken.None)).Status.Should().Be("Cancelled");
            await studio.DeleteProjectAsync(p.Id, CancellationToken.None);
            (await db.AiStudioProjects.AnyAsync(x => x.Id == p.Id)).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task A_video_sent_in_pieces_becomes_a_project()
    {
        var dir = NewDir();
        try
        {
            var bytes = await File.ReadAllBytesAsync(await MakeClipAsync(dir, 3, audio: false));
            var http = _factory.CreateClient();
            var start = await http.PostAsync("/api/ai-studio/uploads", null);
            start.EnsureSuccessStatusCode();
            var uploadId = System.Text.Json.JsonDocument.Parse(await start.Content.ReadAsStringAsync()).RootElement.GetProperty("uploadId").GetString();
            const int piece = 32 * 1024;
            for (var offset = 0; offset < bytes.Length; offset += piece)
            {
                (await http.PutAsync($"/api/ai-studio/uploads/{uploadId}?offset={offset}", new ByteArrayContent(bytes, offset, Math.Min(piece, bytes.Length - offset)))).EnsureSuccessStatusCode();
                if (offset == piece) // a retried piece changes nothing
                    (await http.PutAsync($"/api/ai-studio/uploads/{uploadId}?offset={offset}", new ByteArrayContent(bytes, offset, piece))).EnsureSuccessStatusCode();
            }
            (await http.PutAsync($"/api/ai-studio/uploads/{uploadId}?offset={bytes.Length + 10}", new ByteArrayContent(new byte[10]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var form = new MultipartFormDataContent
            {
                { new StringContent(uploadId!), "uploadId" },
                { new StringContent("Từng khúc.mp4"), "fileName" },
                { new StringContent("video/mp4"), "contentType" }
            };
            var created = await http.PostAsync("/api/ai-studio/projects", form);
            var body = await created.Content.ReadAsStringAsync();
            created.StatusCode.Should().Be(HttpStatusCode.OK, body);
            var json = System.Text.Json.JsonDocument.Parse(body).RootElement;
            json.GetProperty("title").GetString().Should().Be("Từng khúc");
            json.GetProperty("sourceSizeBytes").GetInt64().Should().Be(bytes.Length);
            json.GetProperty("sourceContainer").GetString().Should().Be("mp4");

            using var scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AiStudioService>().DeleteProjectAsync(Guid.Parse(json.GetProperty("id").GetString()!), CancellationToken.None);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>What studio_analyzer.py writes (2 scenes, keyframes as real JPEG files).</summary>
    internal static async Task<string> FakeResultAsync(string dir, string clip, string transcript = "null")
    {
        var media = new AiStudioMedia(new ConfigurationBuilder().Build());
        var frames = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var f = Path.Combine(dir, $"f{i}.jpg");
            await media.ThumbnailAsync(clip, f, i * 0.9, 320, CancellationToken.None);
            frames.Add(f.Replace("\\", "/"));
        }
        string Scene(int index, double start, double end, string f0, string f1, string action) => $$"""
        {"index":{{index}},"start":{{start}},"end":{{end}},"continuation":false,
         "frames":[{"t":{{start + 0.1}},"reason":"start","path":"{{f0}}"},{"t":{{end - 0.1}},"reason":"end","path":"{{f1}}"}],
         "thumb":"{{f0}}",
         "measured":{"camera_movement":["static (locked-off)"],"motion_level":"low","brightness":0.5},
         "colors":{"tone":"warm","contrast":"medium","dominant_colors":[{"hex":"#aa8844","share":0.4}]},
         "analysis":{"summary":"{{action}}","summary_vi":"Cảnh {{index}}","characters":[],"timeline":[{"start":{{start}},"end":{{end}},"action":"{{action}}","body":"steady","confidence":"high"}],
                     "camera":{"angle":"eye-level","shot_size":"wide shot","movement_seen":"static"},"lighting":{"source":"daylight"},"confidence":0.9},
         "confidence":0.88,"transcript":null}
        """;
        var result = $$$"""
        {"engine":{"model":"4b","backend":"vulkan","seconds":12.5},"duration":4,
         "scenes":[{{{Scene(1, 0, 2, frames[0], frames[1], "a ball rolls across the floor")}}},{{{Scene(2, 2, 4, frames[2], frames[3], "the ball stops near a wall")}}}],
         "bibles":{"overview":{"summary":"A ball rolls","summary_vi":"Quả bóng lăn","scene_relations":"same room"},
                   "characters":[],"environments":[{"id":"E1","location":"bright studio room","scenes":[1,2]}],
                   "style":{"visual_style":"clean product video","realism_level":"photorealistic","color_palette":"warm beige","lighting_style":"soft daylight"}},
         "measuredPalette":["#aa8844"],"transcript":{{{transcript}}}}
        """;
        var path = Path.Combine(dir, "result.json");
        await File.WriteAllTextAsync(path, result, Encoding.UTF8);
        return path;
    }

    [Fact]
    public async Task Analysis_results_become_scenes_bibles_and_versioned_prompts()
    {
        var dir = NewDir();
        try
        {
            using var scope = _factory.Services.CreateScope();
            var studio = scope.ServiceProvider.GetRequiredService<AiStudioService>();
            var clip = await MakeClipAsync(dir, 4, audio: false);
            var p = await studio.CreateProjectAsync(clip, "Bóng lăn.mp4", null, CancellationToken.None);
            await studio.IngestResultAsync(p.Id, await FakeResultAsync(dir, clip), CancellationToken.None);

            var a = await studio.GetProjectAsync(p.Id, CancellationToken.None);
            a.Status.Should().Be("Analyzed");
            a.Scenes.Should().HaveCount(2);
            a.Scenes.Select(s => s.Frames.Count).Should().Equal(2, 2);
            a.Scenes.Should().OnlyContain(s => s.HasThumb && s.PromptVersion == 1 && s.Prompt.Contains("Avoid:"));
            a.Scenes[0].Prompt.Should().Contain("a ball rolls across the floor").And.Contain("Setting: bright studio room");
            a.Scenes[0].Measured!["tone"]!.GetValue<string>().Should().Be("warm"); // measured colours merged in
            a.GlobalPrompt.Should().Contain("clean product video");
            a.Palette.Should().Equal("#aa8844");
            a.Scenes[0].SummaryVi.Should().Be("Cảnh 1");
            var frame = a.Scenes[0].Frames[0].BlobId!.Value;
            (await studio.FrameBlobAsync(a.Scenes[0].Id, frame, CancellationToken.None)).Should().Be(frame);
            (await studio.FrameBlobAsync(a.Scenes[1].Id, frame, CancellationToken.None)).Should().BeNull(); // not that scene's file

            // already analysed: not run again unless asked
            var again = () => studio.StartAnalysisAsync(p.Id, false, CancellationToken.None);
            (await again.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Phân tích lại*");

            // the user's own prompt is kept when the mode changes, others follow the mode; every change is a version
            var s1 = a.Scenes[0].Id;
            await studio.EditScenePromptAsync(p.Id, s1, "My own prompt for the first scene", CancellationToken.None);
            var cine = await studio.SetPromptSettingsAsync(p.Id, new AiStudioPromptSettingsDto { Mode = "Cinematic" }, CancellationToken.None);
            cine.Scenes[0].Prompt.Should().Be("My own prompt for the first scene");
            cine.Scenes[0].PromptEdited.Should().BeTrue();
            cine.Scenes[1].Prompt.Should().Contain("cinematic color grade");
            var creative = () => studio.SetPromptSettingsAsync(p.Id, new AiStudioPromptSettingsDto { Mode = "Creative" }, CancellationToken.None);
            (await creative.Should().ThrowAsync<ArgumentException>()).WithMessage("*ít nhất một thay đổi*");

            var versions = await studio.VersionsAsync(p.Id, s1, CancellationToken.None);
            versions.Select(v => v.Source).Should().Equal("user", "engine");
            var restored = await studio.RestoreVersionAsync(p.Id, versions.Last().Id, CancellationToken.None);
            restored.Scenes[0].Prompt.Should().Contain("a ball rolls across the floor");
            restored.Scenes[0].PromptVersion.Should().Be(3);

            // length, on / off and order
            var longer = await studio.UpdateSceneAsync(p.Id, a.Scenes[1].Id, new AiStudioSceneUpdateDto { TargetSeconds = 30, Enabled = false }, CancellationToken.None);
            longer.TargetSeconds.Should().Be(SeedanceVideoClient.MaxSeconds);
            longer.Enabled.Should().BeFalse();
            var reordered = await studio.ReorderAsync(p.Id, new() { a.Scenes[1].Id, a.Scenes[0].Id }, CancellationToken.None);
            reordered.Scenes.Select(s => s.Index).Should().Equal(2, 1);

            await studio.DeleteProjectAsync(p.Id, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>Stands in for the Vercel AI Gateway: every start is counted (= paid), status answers from a table.</summary>
    internal sealed class FakeGateway : HttpMessageHandler
    {
        public byte[] Video { get; set; } = Array.Empty<byte>();
        private int _starts;
        public int Starts => _starts;
        public List<JsonObject> StartBodies { get; } = new();
        /// <summary>Operations whose download fails until <see cref="Heal"/> (a lost download after the job was paid).</summary>
        public HashSet<string> Broken { get; } = new();

        public void Heal() => Broken.Clear();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith("https://blob.test/"))
            {
                var op = url["https://blob.test/".Length..];
                return Broken.Contains(op) ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Video) { Headers = { ContentType = new("video/mp4") } } };
            }
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            if (url.EndsWith("/video-model/start"))
            {
                var n = Interlocked.Increment(ref _starts);
                lock (StartBodies) StartBodies.Add(body);
                var prompt = body["prompt"]!.GetValue<string>();
                var id = prompt.Contains("BROKEN") ? $"broken{n}" : $"op{n}";
                if (prompt.Contains("BROKEN")) Broken.Add(id);
                return Json("{\"operation\":{\"id\":\"" + id + "\"}}");
            }
            if (url.EndsWith("/video-model/status"))
            {
                var id = body["operation"]!["id"]!.GetValue<string>();
                return Json("{\"status\":\"completed\",\"videos\":[{\"type\":\"url\",\"url\":\"https://blob.test/" + id + "\",\"mediaType\":\"video/mp4\"}]}");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };
    }

    private WebApplicationFactory<Program> WithGateway(FakeGateway gw) => _factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Ai:Gateway:ApiKey", "test-gateway-key");
        b.UseSetting("Ai:Gateway:BaseUrl", "https://gw.test/v4/ai");
        b.ConfigureServices(services => services.AddTransient(sp => new SeedanceVideoClient(new HttpClient(gw), sp.GetRequiredService<IConfiguration>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SeedanceVideoClient>.Instance) { PollInterval = TimeSpan.FromMilliseconds(5) }));
    });

    private static async Task WaitAsync(IApplicationDbContext db, Func<Task<bool>> done)
    {
        for (var i = 0; i < 200 && !await done(); i++) await Task.Delay(100);
    }

    [Fact]
    public async Task Seedance_clips_are_confirmed_paid_once_recovered_free_and_joined()
    {
        var dir = NewDir();
        var gw = new FakeGateway();
        try
        {
            gw.Video = await File.ReadAllBytesAsync(await MakeClipAsync(dir, 2, false, "720x1280"));
            await using var app = WithGateway(gw);
            using var scope = app.Services.CreateScope();
            var studio = scope.ServiceProvider.GetRequiredService<AiStudioService>();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var clip = await MakeClipAsync(dir, 4, audio: false);
            var p = await studio.CreateProjectAsync(clip, "Seedance thử.mp4", null, CancellationToken.None);

            // nothing before the analysis
            var early = () => studio.GenerateAsync(p.Id, new AiStudioGenerateRequestDto { SceneIds = new(), ConfirmedCostUsd = 1 }, CancellationToken.None);
            (await early.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Phân tích video xong*");

            await studio.IngestResultAsync(p.Id, await FakeResultAsync(dir, clip), CancellationToken.None);
            var project = await studio.GetProjectAsync(p.Id, CancellationToken.None);
            var ids = project.Scenes.Select(s => s.Id).ToList();
            await studio.SetGeneratorAsync(p.Id, new AiStudioGeneratorSettingsDto { VideoModel = SeedanceVideoClient.FastModel, Resolution = "720p", AspectRatio = "9:16", Seed = 99 }, CancellationToken.None);

            var estimate = await studio.EstimateAsync(p.Id, ids, CancellationToken.None);
            estimate.TotalSeconds.Should().Be(4);
            estimate.TotalUsd.Should().Be(Math.Round(4 * 0.0216m, 4));
            estimate.Blocker.Should().BeNull();

            // a cost higher than the one the user confirmed is refused
            var cheap = () => studio.GenerateAsync(p.Id, new AiStudioGenerateRequestDto { SceneIds = ids, RequestKey = "k0", ConfirmedCostUsd = 0.01m }, CancellationToken.None);
            (await cheap.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*cao hơn số đã xác nhận*");
            gw.Starts.Should().Be(0);

            var costsBefore = await db.StudioCostRecords.CountAsync(c => c.Note!.Contains("Seedance thử"));
            var req = new AiStudioGenerateRequestDto { SceneIds = ids, RequestKey = Guid.NewGuid().ToString(), ConfirmedCostUsd = estimate.TotalUsd };
            await studio.GenerateAsync(p.Id, req, CancellationToken.None);
            await studio.GenerateAsync(p.Id, req, CancellationToken.None); // the same click sent again: nothing new
            var gens = await db.AiStudioGenerations.AsNoTracking().Where(g => g.ProjectId == p.Id).ToListAsync();
            gens.Should().HaveCount(2);
            gens.Should().OnlyContain(g => g.Seed == 99 && g.Seconds == 2 && g.Prompt.Contains("Avoid:"));
            // a scene being made cannot be ordered twice
            var busy = await studio.EstimateAsync(p.Id, ids, CancellationToken.None);
            busy.Lines.Should().OnlyContain(l => l.Skip != null);

            // the jobs may already run in this test host's Hangfire: running them again never pays twice
            foreach (var g in gens) await studio.RunGenerationAsync(g.Id, CancellationToken.None);
            await WaitAsync(db, async () => await db.AiStudioGenerations.AsNoTracking().CountAsync(g => g.ProjectId == p.Id && g.Status == "Done") == 2);
            gw.Starts.Should().Be(2);
            var done = await db.AiStudioGenerations.AsNoTracking().Where(g => g.ProjectId == p.Id).ToListAsync();
            done.Should().OnlyContain(g => g.Status == "Done" && g.ClipBlobId != null && g.ProviderOperation != null && g.CostUsd == Math.Round(2 * 0.0216m, 4));
            (await db.StudioCostRecords.CountAsync(c => c.Note!.Contains("Seedance thử"))).Should().Be(costsBefore + 2);
            gw.StartBodies.Should().OnlyContain(b => b["seed"]!.GetValue<int>() == 99 && b["image"] == null);

            // a paid job whose download is lost: failed, then recovered for free
            var s2 = ids[1];
            await studio.EditScenePromptAsync(p.Id, s2, "BROKEN download test prompt", CancellationToken.None);
            var cost2 = (await studio.EstimateAsync(p.Id, new() { s2 }, CancellationToken.None)).TotalUsd;
            await studio.GenerateAsync(p.Id, new AiStudioGenerateRequestDto { SceneIds = new() { s2 }, RequestKey = Guid.NewGuid().ToString(), ConfirmedCostUsd = cost2 }, CancellationToken.None);
            var broken = await db.AiStudioGenerations.AsNoTracking().Where(g => g.SceneId == s2).OrderByDescending(g => g.CreatedAtUtc).FirstAsync();
            await studio.RunGenerationAsync(broken.Id, CancellationToken.None);
            await WaitAsync(db, async () => (await db.AiStudioGenerations.AsNoTracking().FirstAsync(g => g.Id == broken.Id)).Status == "Failed");
            var failed = await db.AiStudioGenerations.AsNoTracking().FirstAsync(g => g.Id == broken.Id);
            failed.Status.Should().Be("Failed");
            failed.ProviderOperation.Should().NotBeNull();
            failed.ErrorMessage.Should().Contain("Lấy lại clip");
            var startsBefore = gw.Starts;
            gw.Heal();
            (await studio.RecoverAsync(broken.Id, CancellationToken.None)).Status.Should().Be("Recovering");
            await studio.RunRecoverAsync(broken.Id, CancellationToken.None);
            await WaitAsync(db, async () => (await db.AiStudioGenerations.AsNoTracking().FirstAsync(g => g.Id == broken.Id)).Status == "Done");
            (await db.AiStudioGenerations.AsNoTracking().FirstAsync(g => g.Id == broken.Id)).Status.Should().Be("Done");
            gw.Starts.Should().Be(startsBefore); // recovered, not paid again

            // a clip not sent yet can be cancelled; one sent cannot
            var cost1 = (await studio.EstimateAsync(p.Id, new() { ids[0] }, CancellationToken.None)).TotalUsd;
            await studio.GenerateAsync(p.Id, new AiStudioGenerateRequestDto { SceneIds = new() { ids[0] }, RequestKey = Guid.NewGuid().ToString(), ConfirmedCostUsd = cost1 }, CancellationToken.None);
            var pending = await db.AiStudioGenerations.AsNoTracking().Where(g => g.SceneId == ids[0]).OrderByDescending(g => g.CreatedAtUtc).FirstAsync();
            var cancelled = await studio.CancelGenerationAsync(pending.Id, CancellationToken.None);
            cancelled.Status.Should().BeOneOf("Cancelled", "Done", "Generating"); // Hangfire may have taken it first

            // join the latest clip of every scene in order
            await WaitAsync(db, async () => !await db.AiStudioGenerations.AsNoTracking().AnyAsync(g => g.ProjectId == p.Id && (g.Status == "Queued" || g.Status == "Generating")));
            await studio.AssembleAsync(p.Id, CancellationToken.None);
            await studio.RunAssembleAsync(p.Id, CancellationToken.None);
            await WaitAsync(db, async () => (await db.AiStudioProjects.AsNoTracking().FirstAsync(x => x.Id == p.Id)).FinalStatus != "Assembling");
            var final = await studio.GetProjectAsync(p.Id, CancellationToken.None);
            final.FinalStatus.Should().Be("Done", final.FinalError);
            final.HasFinal.Should().BeTrue();

            var history = await studio.GenerationsAsync(p.Id, CancellationToken.None);
            history.Should().HaveCountGreaterThanOrEqualTo(3);
            history.Should().OnlyContain(h => h.ProjectTitle == "Seedance thử");

            await studio.DeleteProjectAsync(p.Id, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>Stands in for Gemini TTS: a take is a tone as long as the line needs; every call is counted (= paid).</summary>
    internal sealed class FakeNarrator : IAiStudioNarrator
    {
        public int Calls;
        public List<(string Text, string Voice, string? Style)> Takes { get; } = new();
        public decimal PricePerSecond => 0.0003m;

        public async Task<(byte[] Audio, double Seconds, bool FromCache, string? Delivery)> SpeakAsync(Guid projectId, string text, string voice, string? style,
            string? fallbackVoice, CancellationToken ct, string? speed = null)
        {
            Interlocked.Increment(ref Calls);
            lock (Takes) Takes.Add((text, voice, style));
            var seconds = text.Contains("nghèo") ? 1.5 : text.Contains("DÀI") ? (speed == "+20%" ? 13.0 : 16.0) : 3.0;
            var path = Path.Combine(Path.GetTempPath(), $"take-{Guid.NewGuid():N}.m4a");
            var media = new AiStudioMedia(new ConfigurationBuilder().Build());
            var (code, _, err) = await AiStudioMedia.RunAsync(media.Ffmpeg, new[] { "-hide_banner", "-v", "error", "-y", "-f", "lavfi", "-i",
                $"sine=frequency=220:duration={seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}", "-ac", "1", "-c:a", "aac", path }, ct);
            code.Should().Be(0, err);
            try { return (await File.ReadAllBytesAsync(path, ct), seconds, false, "diễn cảm"); }
            finally { File.Delete(path); }
        }
    }

    private const string ChildhoodTranscript = """
    {"language":"vi","model":"ggml-small.bin","segments":[
      {"start":0.2,"end":1.6,"text":"Hồi đó nhà tôi nghèo lắm.","en":"Back then my family was very poor."},
      {"start":2.2,"end":3.6,"text":"Mẹ đi chợ từ sáng sớm.","en":"Mother went to the market at dawn."}],
     "voice":{"pitch_hz":112.4,"syllables_per_second":3.1,"loudness_std_db":5.2}}
    """;

    [Fact]
    public async Task The_childhood_theme_reads_the_narration_again_with_an_ai_voice_in_the_joined_video()
    {
        var dir = NewDir();
        var gw = new FakeGateway();
        var narrator = new FakeNarrator();
        try
        {
            gw.Video = await File.ReadAllBytesAsync(await MakeClipAsync(dir, 2, false, "720x1280"));
            await using var app = WithGateway(gw).WithWebHostBuilder(b => b.ConfigureServices(services => services.AddScoped<IAiStudioNarrator>(_ => narrator)));
            using var scope = app.Services.CreateScope();
            var studio = scope.ServiceProvider.GetRequiredService<AiStudioService>();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var clip = await MakeClipAsync(dir, 4, audio: true);

            var silent = await studio.CreateProjectAsync(clip, "Gái xinh.mp4", null, CancellationToken.None, AiStudioTopics.AsianBeauty);
            silent.AudioMode.Should().Be("None");
            silent.HasSourceAudio.Should().BeTrue();
            await studio.DeleteProjectAsync(silent.Id, CancellationToken.None);

            var title = "Ký ức " + Guid.NewGuid().ToString("N")[..8];
            var p = await studio.CreateProjectAsync(clip, "Ký ức.mp4", title, CancellationToken.None, AiStudioTopics.VietnamChildhood);
            p.Topic.Should().Be(AiStudioTopics.VietnamChildhood);
            p.AudioMode.Should().Be("Narration");
            await studio.IngestResultAsync(p.Id, await FakeResultAsync(dir, clip, ChildhoodTranscript), CancellationToken.None);

            // 1. what is said in each scene, its meaning in the prompt, a narrator like the reference speaker
            var project = await studio.GetProjectAsync(p.Id, CancellationToken.None);
            project.Scenes[0].Narration.Should().Be("Hồi đó nhà tôi nghèo lắm.");
            project.Scenes[1].Narration.Should().Be("Mẹ đi chợ từ sáng sớm.");
            project.Scenes[0].Prompt.Should().Contain("\"Back then my family was very poor\"");
            project.Scenes[1].Prompt.Should().Contain("\"Mother went to the market at dawn\"");
            project.NarratorVoice.Should().Be("gemini:Algenib");
            project.NarratorStyle.Should().Contain("slowly and calmly").And.Contain("Vietnamese countryside");
            project.SpeakerProfileVi.Should().StartWith("Giọng nam");
            project.Scenes.Should().OnlyContain(s => !s.HasVoice && !s.VoiceCurrent);

            // 2. no join before the narration is read
            var early = () => studio.AssembleAsync(p.Id, CancellationToken.None);
            var ids = project.Scenes.Select(s => s.Id).ToList();
            var cost = (await studio.EstimateAsync(p.Id, ids, CancellationToken.None)).TotalUsd;
            await studio.GenerateAsync(p.Id, new AiStudioGenerateRequestDto { SceneIds = ids, RequestKey = Guid.NewGuid().ToString(), ConfirmedCostUsd = cost }, CancellationToken.None);
            foreach (var g in await db.AiStudioGenerations.AsNoTracking().Where(g => g.ProjectId == p.Id).ToListAsync())
                await studio.RunGenerationAsync(g.Id, CancellationToken.None);
            await WaitAsync(db, async () => await db.AiStudioGenerations.AsNoTracking().CountAsync(g => g.ProjectId == p.Id && g.Status == "Done") == 2);
            (await early.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Cảnh 1, 2 chưa đọc lời bình*");

            // 3. read: the narrator's voice and direction, the cost recorded, the clip lengthened to its take
            var first = await studio.SpeakSceneAsync(p.Id, ids[0], CancellationToken.None);
            var second = await studio.SpeakSceneAsync(p.Id, ids[1], CancellationToken.None);
            narrator.Calls.Should().Be(2);
            narrator.Takes.Should().OnlyContain(t => t.Voice == "gemini:Algenib" && t.Style!.Contains("storyteller"));
            first.VoiceCurrent.Should().BeTrue();
            first.VoiceSeconds.Should().Be(1.5);
            first.TargetSeconds.Should().Be(2);           // the take fits the 2 s clip
            second.TargetSeconds.Should().Be(4);          // a 3 s take needs a 4 s clip
            second.Prompt.Should().Contain("0–4s");       // the timed sequence follows the new length
            (await db.StudioCostRecords.AsNoTracking().CountAsync(c => c.Kind == "Voice" && c.Note!.Contains(title) && c.Note.Contains("lời bình cảnh"))).Should().Be(2);
            await studio.SpeakSceneAsync(p.Id, ids[0], CancellationToken.None);
            narrator.Calls.Should().Be(2);                // same words, voice and style: never paid twice

            // 3b. a take too long for one clip is read again 20 % faster (both takes recorded)
            await studio.EditNarrationAsync(p.Id, ids[1], "Lời bình DÀI quá một clip.", CancellationToken.None);
            var fast = await studio.SpeakSceneAsync(p.Id, ids[1], CancellationToken.None);
            narrator.Calls.Should().Be(4);
            fast.VoiceSeconds.Should().Be(13);
            fast.TargetSeconds.Should().Be(SeedanceVideoClient.MaxSeconds);
            (await db.StudioCostRecords.AsNoTracking().CountAsync(c => c.Note!.Contains(title) && c.Note.Contains("đọc nhanh hơn"))).Should().Be(1);
            await studio.EditNarrationAsync(p.Id, ids[1], "Mẹ đi chợ từ sáng sớm.", CancellationToken.None);
            await studio.SpeakSceneAsync(p.Id, ids[1], CancellationToken.None);
            narrator.Calls.Should().Be(5);

            // 4. the user's own words: quoted in the prompt, the take out of date until read again
            var edited = await studio.EditNarrationAsync(p.Id, ids[0], "Hồi đó nhà tôi nghèo lắm, cơm không đủ ăn.", CancellationToken.None);
            edited.NarrationEdited.Should().BeTrue();
            edited.VoiceCurrent.Should().BeFalse();
            edited.Prompt.Should().Contain("\"Hồi đó nhà tôi nghèo lắm, cơm không đủ ăn\"");
            await studio.SpeakSceneAsync(p.Id, ids[0], CancellationToken.None);
            narrator.Calls.Should().Be(6);

            // the channel's designed voices ("Giọng đọc" tab) are offered and usable
            var designedKey = NewsStudio.Infrastructure.Studio.StudioService.DesignedVoicesKey;
            var hadDesigned = await db.SystemSettings.AnyAsync(x => x.SettingKey == designedKey);
            if (!hadDesigned)
            {
                db.SystemSettings.Add(new NewsStudio.Domain.Entities.SystemSetting { SettingKey = designedKey, Group = "Studio",
                    SettingValue = """[{"id":"gemini:voice_anh3","name":"Anh 3","gender":"male","description":"Giọng miền Tây ấm"}]""" });
                await db.SaveChangesAsync(CancellationToken.None);
            }
            try
            {
                var offered = (await studio.NarratorVoicesAsync(CancellationToken.None));
                offered.Should().Contain(v => v.Designed).And.Contain(v => v.Id == "gemini:Charon");
                var own = offered.First(v => v.Designed).Id;
                (await studio.SetNarratorAsync(p.Id, new AiStudioNarratorDto { Voice = own }, CancellationToken.None)).NarratorVoice.Should().Be(own);
                var bad = () => studio.SetNarratorAsync(p.Id, new AiStudioNarratorDto { Voice = "gemini:voice_khong_co" }, CancellationToken.None);
                await bad.Should().ThrowAsync<ArgumentException>();
                await studio.SetNarratorAsync(p.Id, new AiStudioNarratorDto { Voice = "gemini:Algenib" }, CancellationToken.None);
            }
            finally
            {
                if (!hadDesigned)
                {
                    db.SystemSettings.Remove(await db.SystemSettings.FirstAsync(x => x.SettingKey == designedKey));
                    await db.SaveChangesAsync(CancellationToken.None);
                }
            }

            // 5. joined: the clips with the AI narration (the reference speaker's own voice is not used)
            await studio.AssembleAsync(p.Id, CancellationToken.None);
            await studio.RunAssembleAsync(p.Id, CancellationToken.None);
            var row = await db.AiStudioProjects.AsNoTracking().FirstAsync(x => x.Id == p.Id);
            row.FinalStatus.Should().Be("Done", row.FinalError);
            var final = Path.Combine(dir, "final.mp4");
            var store = scope.ServiceProvider.GetRequiredService<StudioFileStore>();
            (await store.CopyToAsync(row.FinalBlobId!.Value, final, CancellationToken.None)).Should().BeTrue();
            var media = new AiStudioMedia(new ConfigurationBuilder().Build());
            var probe = await media.ProbeAsync(final, CancellationToken.None);
            probe.Audio.Should().ContainSingle();
            probe.Duration.Should().BeApproximately(4, 0.3);

            // 6. the reference's own sound is still available on request
            await studio.SetGeneratorAsync(p.Id, new AiStudioGeneratorSettingsDto { VideoModel = SeedanceVideoClient.FastModel, Resolution = "720p", AspectRatio = "9:16", AudioMode = "Original" }, CancellationToken.None);
            await studio.AssembleAsync(p.Id, CancellationToken.None);
            await studio.RunAssembleAsync(p.Id, CancellationToken.None);
            row = await db.AiStudioProjects.AsNoTracking().FirstAsync(x => x.Id == p.Id);
            row.FinalStatus.Should().Be("Done", row.FinalError);
            (await store.CopyToAsync(row.FinalBlobId!.Value, final, CancellationToken.None)).Should().BeTrue();
            (await media.ProbeAsync(final, CancellationToken.None)).Audio.Should().ContainSingle();

            // switching the theme off turns the sound off too
            var off = await studio.SetPromptSettingsAsync(p.Id, new AiStudioPromptSettingsDto { Mode = "Faithful", Topic = "" }, CancellationToken.None);
            off.AudioMode.Should().Be("None");
            var voices = await db.AiStudioScenes.AsNoTracking().Where(s => s.ProjectId == p.Id && s.VoiceBlobId != null).Select(s => s.VoiceBlobId!.Value).ToListAsync();
            voices.Should().HaveCount(2);
            await studio.DeleteProjectAsync(p.Id, CancellationToken.None);
            (await db.StudioBlobs.AsNoTracking().CountAsync(b => voices.Contains(b.Id))).Should().Be(0);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// The real local analysis (FFmpeg + OpenCV + Qwen3-VL on this PC). Takes minutes, so it runs only with
    /// AI_STUDIO_E2E=1 and when scripts/pc/setup-ai-studio.bat has been run on the machine.
    /// </summary>
    [Fact]
    public async Task The_pc_analyzer_finds_the_cuts_and_describes_each_scene()
    {
        using var scope = _factory.Services.CreateScope();
        var studio = scope.ServiceProvider.GetRequiredService<AiStudioService>();
        var engine = scope.ServiceProvider.GetRequiredService<AiStudioEngine>();
        if (Environment.GetEnvironmentVariable("AI_STUDIO_E2E") != "1" || !engine.Paths.Installed) return;

        var dir = NewDir();
        try
        {
            // two different shots joined: a cut at 3 s
            var a = await MakeClipAsync(dir, 3, false, "360x640");
            var media = new AiStudioMedia(new ConfigurationBuilder().Build());
            var b = Path.Combine(dir, "b.mp4");
            (await AiStudioMedia.RunAsync(media.Ffmpeg, new[] { "-hide_banner", "-v", "error", "-y", "-f", "lavfi", "-i", "mandelbrot=size=360x640:rate=24", "-t", "3", "-pix_fmt", "yuv420p", b }, CancellationToken.None)).Code.Should().Be(0);
            var joined = Path.Combine(dir, "joined.mp4");
            (await AiStudioMedia.RunAsync(media.Ffmpeg, new[] { "-hide_banner", "-v", "error", "-y", "-i", a, "-i", b, "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0[v]", "-map", "[v]", "-c:v", "libx264", "-pix_fmt", "yuv420p", joined }, CancellationToken.None)).Code.Should().Be(0);

            var p = await studio.CreateProjectAsync(joined, "Hai cảnh.mp4", null, CancellationToken.None);
            await studio.StartAnalysisAsync(p.Id, false, CancellationToken.None);
            await studio.RunAnalysisAsync(p.Id, CancellationToken.None);
            var done = await studio.GetProjectAsync(p.Id, CancellationToken.None);
            done.Status.Should().Be("Analyzed", done.ErrorMessage);
            done.Scenes.Should().HaveCount(2);
            done.Scenes[0].EndSeconds.Should().BeApproximately(3, 0.3);
            done.Scenes.Should().OnlyContain(s => s.Frames.Count >= 3 && s.Analysis!["timeline"]!.AsArray().Count >= 1 && s.Prompt.Length > 200);
            await studio.DeleteProjectAsync(p.Id, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
