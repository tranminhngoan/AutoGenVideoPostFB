using System.Text.RegularExpressions;
using NewsStudio.Application.DTOs;

namespace NewsStudio.Infrastructure.Studio;

/// <summary>
/// Turns a finished script from the script library (/drama-scripts: hook, ending, mood, and scenes with an English
/// video prompt, a Vietnamese narration, sound notes and a camera) into studio shots that follow it exactly — no AI
/// planning needed. A scene lasts 15–30 s but a Seedance clip at most 12 s, so each scene becomes several shots on the
/// same prompt with varied framing, and its narration is split by sentence across them (each piece short enough to
/// be read within one clip). The hook opens the first scene and the ending closes the last.
/// </summary>
public static class StudioScriptImporter
{
    /// <summary>
    /// Longest narration piece for one clip: ~24 words. Emotional narration is read slowly (a 28-word line took 11.4 s at
    /// -8 %), so this leaves room in a 12 s clip.
    /// </summary>
    public const int MaxWordsPerShot = 24;
    private const int MaxShotsPerScene = 6;

    /// <summary>Extra framings for the 2nd, 3rd… shot of a scene, so one long scene doesn't repeat the same picture.</summary>
    private static readonly string[] Variations =
    {
        "closer medium shot on the people and their gestures, gentle handheld camera",
        "intimate detail close-up of hands and the key objects, shallow depth of field",
        "slow push-in on faces showing the emotion",
        "wide shot revealing the surroundings and the atmosphere",
        "low-angle side view with soft natural light, slow camera glide",
    };

    public sealed record ImportedShot(int Scene, int Duration, string Action, string Camera, string? Dialogue, string? Sound);

    /// <summary>
    /// How the narrator performs a script of this mood (English direction for the TTS model, sent apart from the
    /// words). The preparation step may refine it per scene.
    /// </summary>
    public static string NarratorStyleFor(string? mood)
    {
        var m = (mood ?? "").ToLowerInvariant();
        var core =
            m.Contains("nghẹn") || m.Contains("nước mắt") ? "a gentle Vietnamese storyteller remembering a poor childhood, slow and tender, moved but composed — never crying or whimpering"
            : m.Contains("da diết") || m.Contains("day dứt") || m.Contains("nhớ cha mẹ") ? "a warm Vietnamese storyteller remembering late parents, heartfelt, slow and reflective, longing in the voice"
            : m.Contains("xót xa") || m.Contains("thương quê") ? "a Vietnamese storyteller telling a bittersweet memory of the home village, low, sincere and compassionate, unhurried"
            : m.Contains("hóm hỉnh") || m.Contains("ngây ngô") || m.Contains("vui") ? "a playful Vietnamese storyteller recalling childhood mischief, light and smiling, lively but natural rhythm"
            : m.Contains("ấm áp") || m.Contains("hoài niệm") ? "a warm, gentle Vietnamese storyteller telling a cozy nostalgic memory, a soft smile in the voice, calm and intimate"
            : "a warm, sincere Vietnamese storyteller sharing a nostalgic memory, intimate and unhurried";
        return core + "; natural native Vietnamese, rustic and sincere, like chatting with neighbours on the porch, steady voice, real breaths and pauses " +
               "between sentences; not melodramatic, not tearful, never like a newsreader or a robot";
    }

    /// <summary>Narrator voice and reading speed that suit the script's mood (the user can change both).</summary>
    public static (string Voice, string Gender, string Speed) NarratorFor(string? mood)
    {
        // Sulafat (warm, tender) reads every emotional script; only a playful one gets a lighter voice
        var m = (mood ?? "").ToLowerInvariant();
        if (m.Contains("hóm hỉnh") || m.Contains("ngây ngô") || m.Contains("vui")) return ("gemini:Puck", "male", "+0%");
        if (m.Contains("nghẹn") || m.Contains("nước mắt")) return ("gemini:Sulafat", "female", "-8%");
        if (m.Contains("da diết") || m.Contains("day dứt") || m.Contains("nhớ cha mẹ") || m.Contains("xót xa") || m.Contains("thương quê"))
            return ("gemini:Sulafat", "female", "-6%");
        return ("gemini:Sulafat", "female", "-4%");
    }

    /// <summary>Look of the whole film from the script's era / region (kept short; each prompt carries the details).</summary>
    public static string StyleFor(StudioScriptImportDto s)
    {
        var era = s.Era?.Trim() switch { "8x" => "late 1980s", "9x" => "1990s", _ => "1980s–1990s" };
        var region = string.IsNullOrWhiteSpace(s.Region) || s.Region == "Chung" ? "rural Vietnam" : $"rural {s.Region} Vietnam";
        return $"{era} {region}, realistic 35mm film footage, natural daylight colours, authentic period details";
    }

    /// <summary>
    /// Splits narration into pieces of at most <paramref name="maxWords"/> words: whole sentences where possible, then
    /// clauses at commas, then plain word runs — never cutting a word.
    /// </summary>
    public static List<string> SplitNarration(string? text, int maxWords = MaxWordsPerShot)
    {
        var pieces = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return pieces;
        static int Words(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        var sentences = Regex.Split(text.Trim(), @"(?<=[\.\!\?…;:])\s+(?=[^\s])")
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        var units = new List<string>();
        foreach (var sentence in sentences)
        {
            if (Words(sentence) <= maxWords) { units.Add(sentence); continue; }
            var clauses = Regex.Split(sentence, @"(?<=,)\s+").Select(c => c.Trim()).Where(c => c.Length > 0);
            foreach (var clause in clauses)
            {
                if (Words(clause) <= maxWords) { units.Add(clause); continue; }
                var words = clause.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < words.Length; i += maxWords) units.Add(string.Join(' ', words.Skip(i).Take(maxWords)));
            }
        }
        // pack short units together so each clip carries a natural amount of speech
        var current = "";
        foreach (var u in units)
        {
            var joined = current.Length == 0 ? u : current + " " + u;
            if (current.Length > 0 && Words(joined) > maxWords)
            {
                pieces.Add(current);
                current = u;
            }
            else current = joined;
        }
        if (current.Length > 0) pieces.Add(current);
        return pieces;
    }

    /// <summary>The scene's narration, with the hook in front of the first scene and the ending after the last.</summary>
    public static string NarrationOf(StudioScriptImportDto s, int sceneIndex)
    {
        var scene = s.Scenes[sceneIndex];
        var text = (scene.Voiceover ?? "").Trim();
        static bool Has(string haystack, string? needle) =>
            !string.IsNullOrWhiteSpace(needle) && Normalize(haystack).Contains(Normalize(needle));
        if (sceneIndex == 0 && !string.IsNullOrWhiteSpace(s.Hook) && !Has(text, s.Hook)) text = $"{s.Hook!.Trim()} {text}".Trim();
        if (sceneIndex == s.Scenes.Count - 1 && !string.IsNullOrWhiteSpace(s.Ending) && !Has(text, s.Ending)) text = $"{text} {s.Ending!.Trim()}".Trim();
        return text;
    }

    private static string Normalize(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    /// <summary>A library prompt written for still-image tools, reworded for motion ("film still" → "film footage").</summary>
    public static string MotionPrompt(string prompt) =>
        Regex.Replace(Regex.Replace(prompt.Trim(), @"\bfilm still\b", "film footage", RegexOptions.IgnoreCase),
            @"\b(photorealistic )?8k( resolution)?\b", "photorealistic", RegexOptions.IgnoreCase);

    public static List<ImportedShot> BuildShots(StudioScriptImportDto s)
    {
        var shots = new List<ImportedShot>();
        for (var i = 0; i < s.Scenes.Count; i++)
        {
            var scene = s.Scenes[i];
            var prompt = MotionPrompt(string.IsNullOrWhiteSpace(scene.PromptEn) ? scene.VisualVi ?? scene.Title ?? "" : scene.PromptEn!);
            if (prompt.Length == 0) continue;
            var narration = NarrationOf(s, i);
            var pieces = SplitNarration(narration);
            var sceneSeconds = Math.Max(scene.DurationSeconds, 4);
            var wanted = Math.Clamp((int)Math.Ceiling(sceneSeconds / 10.0), 1, MaxShotsPerScene);
            // the scene is read in ONE take running over its shots: rather than silent shots between the words,
            // cut the narration finer so every picture carries (and illustrates) a piece of it
            for (var max = MaxWordsPerShot - 4; pieces.Count < wanted && max >= 8; max -= 4) pieces = SplitNarration(narration, max);
            var count = Math.Clamp(Math.Max(pieces.Count, wanted), 1, MaxShotsPerScene);
            count = Math.Max(count, pieces.Count); // never drop narration
            // spoken pieces first, in order; any silent picture beats close the scene after the words
            var lineFor = new string?[count];
            for (var p = 0; p < pieces.Count; p++) lineFor[p] = pieces[p];
            var spoken = lineFor.Where(l => l != null).Sum(l => StudioService.SecondsToSay(l));
            var silentShots = lineFor.Count(l => l == null);
            var silentLength = silentShots == 0 ? 0 : Math.Clamp((int)Math.Round((sceneSeconds - spoken) / (double)silentShots), 4, 8);
            for (var k = 0; k < count; k++)
            {
                var line = lineFor[k];
                var duration = line != null ? Math.Max(4, StudioService.SecondsToSay(line)) : silentLength;
                var camera = k == 0 ? (scene.Camera ?? "").Trim() : Variations[(k - 1) % Variations.Length];
                shots.Add(new ImportedShot(i, SeedanceVideoClient.ClampSeconds(duration), prompt, camera, line, NullIfEmpty(scene.Sound)));
            }
        }
        return shots;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
