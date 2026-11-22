using FluentAssertions;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.FilmFactory;
using Xunit;

namespace NewsStudio.Tests;

/// <summary>Which shots get lip sync, and what the motion model is told about mouths.</summary>
public class FilmShotPlannerTests : IDisposable
{
    private readonly string _voice = Path.Combine(Path.GetTempPath(), $"planner_voice_{Guid.NewGuid():N}.wav");

    public FilmShotPlannerTests() => File.WriteAllBytes(_voice, new byte[16]);

    public void Dispose()
    {
        try { File.Delete(_voice); } catch { /* best effort */ }
    }

    private FilmShot Speaking(string present = "char_a", string speaker = "char_a", string shotType = "CloseUp") => new()
    {
        UseMotion = true,
        DialogueText = "Tôi đã nói với anh chuyện này rất nhiều lần rồi.",
        SpeakerCharacterId = speaker,
        CharacterIdsPresent = present,
        ShotType = shotType,
        AudioVoicePath = _voice,
        DurationSeconds = 3.4,
        MotionPrompt = "she frowns and leans forward"
    };

    [Fact]
    public void Sole_visible_speaker_in_a_close_shot_is_lip_synced() =>
        FilmShotPlanner.IsOnCameraSpeech(Speaking()).Should().BeTrue();

    [Theory]
    [InlineData("char_a,char_b", "char_a", "CloseUp")]        // two faces: the model could sync the listener
    [InlineData("char_b", "char_a", "CloseUp")]               // off-screen speaker over a reaction shot
    [InlineData("char_a", "narrator", "CloseUp")]             // voice-over
    [InlineData("char_a", "char_a", "WideShot")]              // mouth too small
    public void Other_dialogue_shots_are_not_lip_synced(string present, string speaker, string shotType) =>
        FilmShotPlanner.IsOnCameraSpeech(Speaking(present, speaker, shotType)).Should().BeFalse();

    [Fact]
    public void Missing_voice_file_means_no_lip_sync()
    {
        var shot = Speaking();
        shot.AudioVoicePath = Path.Combine(Path.GetTempPath(), "does_not_exist.wav");
        FilmShotPlanner.IsOnCameraSpeech(shot).Should().BeFalse();
    }

    [Fact]
    public void With_lip_sync_off_prompts_are_unchanged_from_the_previous_pipeline()
    {
        var (prompt, negative) = FilmShotPlanner.MotionPrompts(Speaking(), lipSyncEnabled: false);
        prompt.Should().Be("she frowns and leans forward, smooth natural motion, stable face and eyes, highly detailed photorealistic, cinematic camera");
        negative.Should().Be("worst quality, blurry, jittery, distorted face, deformed face, morphing face, melting face, asymmetrical eyes, open mouth, screaming, grimace, deformed, extra limbs, morphing body, text, watermark");
        FilmShotPlanner.MotionSeconds(Speaking(), lipSync: false).Should().Be(4.0);
    }

    [Fact]
    public void On_camera_speaker_is_told_to_talk_and_not_forbidden_to_open_the_mouth()
    {
        var (prompt, negative) = FilmShotPlanner.MotionPrompts(Speaking(), lipSyncEnabled: true);
        prompt.Should().Contain("speaking");
        negative.Should().NotContain("open mouth").And.NotContain("talking");
    }

    [Fact]
    public void Listener_keeps_the_mouth_still_while_someone_else_speaks()
    {
        var (_, negative) = FilmShotPlanner.MotionPrompts(Speaking(present: "char_b"), lipSyncEnabled: true);
        negative.Should().Contain("open mouth").And.Contain("talking");
    }

    [Fact]
    public void Synced_clip_asks_for_the_voice_length_capped_at_81_frames()
    {
        FilmShotPlanner.MotionSeconds(Speaking(), lipSync: true).Should().Be(3.4);
        var longLine = Speaking();
        longLine.DurationSeconds = 9;
        FilmShotPlanner.MotionSeconds(longLine, lipSync: true).Should().Be(5.1);
    }
}
