using NewsStudio.Domain.Entities;

namespace NewsStudio.Infrastructure.FilmFactory;

/// <summary>
/// Separates what each GPU stage is asked to do for a shot (motion vs. lip sync), so the motion model is
/// never asked to "act out" dialogue it cannot hear and the lip-sync model only touches the face that speaks.
/// </summary>
public static class FilmShotPlanner
{
    /// <summary>Visual method of a shot whose clip already carries mouth movement matched to its voice.</summary>
    public const string LipSyncMethod = "LipSyncMotion";
    public const string DefaultMethod = "CinematicMotionImage";

    /// <summary>Wan 2.1 is trained on 81 frames at 16 fps.</summary>
    public const double MaxMotionSeconds = 81 / 16.0;

    public const string BaseNegative =
        "worst quality, blurry, jittery, distorted face, deformed face, morphing face, melting face, asymmetrical eyes, " +
        "screaming, grimace, deformed, extra limbs, morphing body, text, watermark";

    /// <summary>
    /// True when the line is spoken on camera by the only face in the frame: a character (not the narrator)
    /// speaks, that character is the one visible character, the shot is not a wide shot (mouth too small to
    /// sync) and the recorded voice exists. Two-face frames are excluded because the lip-sync model picks a
    /// face by detection and could move the listener's mouth.
    /// </summary>
    public static bool IsOnCameraSpeech(FilmShot shot)
    {
        if (!shot.UseMotion || string.IsNullOrWhiteSpace(shot.DialogueText)) return false;
        var speaker = shot.SpeakerCharacterId;
        if (string.IsNullOrWhiteSpace(speaker) || speaker.Equals(FilmShowrunnerService.NarratorId, StringComparison.OrdinalIgnoreCase))
            return false;
        var present = (shot.CharacterIdsPresent ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (present.Length != 1 || !present[0].Equals(speaker, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(shot.ShotType, "WideShot", StringComparison.OrdinalIgnoreCase)) return false;
        return !string.IsNullOrWhiteSpace(shot.AudioVoicePath) && File.Exists(shot.AudioVoicePath);
    }

    /// <summary>
    /// Motion prompt and negative prompt. With lip sync on, an on-camera speaker is told to talk (jaw and lips
    /// move, the lip-sync stage then matches them to the voice) and every other visible face is told to keep
    /// its mouth still, so nobody "mouths" narration or the other person's line.
    /// With lip sync off the prompts are exactly the previous ones.
    /// </summary>
    public static (string Prompt, string Negative) MotionPrompts(FilmShot shot, bool lipSyncEnabled)
    {
        var basePrompt = (string.IsNullOrWhiteSpace(shot.MotionPrompt) ? "subtle natural movement" : shot.MotionPrompt!)
                         + ", smooth natural motion, stable face and eyes, highly detailed photorealistic, cinematic camera";
        if (!lipSyncEnabled)
            return (basePrompt, BaseNegative.Replace("screaming,", "open mouth, screaming,"));

        if (IsOnCameraSpeech(shot))
            return (basePrompt + ", the person is speaking to someone, natural jaw and lip movement while talking, natural eye blinks, subtle head movement",
                    BaseNegative);

        var facesVisible = !string.IsNullOrWhiteSpace(shot.CharacterIdsPresent);
        return (basePrompt, facesVisible
            ? BaseNegative + ", open mouth, talking, speaking, mouth moving, lip movement"
            : BaseNegative);
    }

    /// <summary>
    /// Requested clip length. A synced shot is not trimmed or slowed later, so it asks for the voice length
    /// (capped at the model's 81 frames; LatentSync loops the clip back and forth for longer lines).
    /// Other shots keep the previous rule (renderer keeps 85% of the clip).
    /// </summary>
    public static double MotionSeconds(FilmShot shot, bool lipSync) =>
        Math.Round(lipSync
            ? Math.Clamp(shot.DurationSeconds, 2, MaxMotionSeconds)
            : Math.Clamp(shot.DurationSeconds / 0.85, 2, 5), 1);
}
