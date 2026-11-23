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
    /// <summary>Longest narration piece for one clip: ~28 words ≈ 10.3 s read, leaving room in a 12 s clip.</summary>
    public const int MaxWordsPerShot = 28;
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

    /// <summary>Narrator voice and reading speed that suit the script's mood (the user can change both).</summary>
    public static (string Voice, string Gender, string Speed) NarratorFor(string? mood)
    {
        var m = (mood ?? "").ToLowerInvariant();
        if (m.Contains("nghẹn") || m.Contains("nước mắt")) return ("gemini:Sulafat", "female", "-8%");
        if (m.Contains("da diết") || m.Contains("day dứt") || m.Contains("nhớ cha mẹ")) return ("gemini:Charon", "male", "-6%");
        if (m.Contains("xót xa") || m.Contains("thương quê")) return ("gemini:Algenib", "male", "-6%");
        if (m.Contains("hóm hỉnh") || m.Contains("ngây ngô") || m.Contains("vui")) return ("gemini:Puck", "male", "+0%");
        if (m.Contains("ấm áp") || m.Contains("hoài niệm")) return ("gemini:Aoede", "female", "-4%");
        return ("gemini:Charon", "male", "-4%");
    }

    /// <summary>Look of the whole film from the script's era / region (kept short; each prompt carries the details).</summary>
    public static string StyleFor(StudioScriptImportDto s)
    {
        var era = s.Era?.Trim() switch { "8x" => "late 1980s", "9x" => "1990s", _ => "1980s–1990s" };
        var region = string.IsNullOrWhiteSpace(s.Region) || s.Region == "Chung" ? "rural Vietnam" : $"rural {s.Region} Vietnam";
        return $"{era} {region}, nostalgic 35mm film footage, warm natural light, authentic period details";
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
            var pieces = SplitNarration(NarrationOf(s, i));
            var sceneSeconds = Math.Max(scene.DurationSeconds, 4);
            var count = Math.Clamp(Math.Max(pieces.Count, (int)Math.Ceiling(sceneSeconds / 10.0)), 1, MaxShotsPerScene);
            count = Math.Max(count, pieces.Count); // never drop narration
            // spread the spoken pieces over the scene's shots; the rest are silent picture beats
            var lineFor = new string?[count];
            for (var p = 0; p < pieces.Count; p++) lineFor[(int)Math.Floor(p * (double)count / pieces.Count)] = pieces[p];
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
