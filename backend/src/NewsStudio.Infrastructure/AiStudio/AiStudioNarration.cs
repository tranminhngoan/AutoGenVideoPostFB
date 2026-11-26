using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Infrastructure.AiStudio;

/// <summary>
/// The narration of a reference video, re-made: what is said in each scene (speech recognised on the PC, editable),
/// its English meaning for the Seedance prompt, and a narrator voice + acting direction chosen to sound like the
/// reference speaker (measured pitch, pace and liveliness) and the channel theme. The narration is then read again by
/// Gemini TTS — the reference speaker's own voice is never reused.
/// </summary>
public static class AiStudioNarration
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The longest take that still fits one clip once played up to 25 % faster at assembly.</summary>
    public const double MaxVoiceSeconds = 14.5;

    /// <summary>Speech one scene may hold before its sentences are split with the neighbouring scenes (a clip is at most 12 s).</summary>
    public const double MaxSceneSpeechSeconds = 10.5;

    public sealed record Word(double Start, double End, string Text);

    /// <summary>A spoken line (sentence) with its words' times; older transcripts have none (spread evenly then).</summary>
    public sealed record Line(double Start, double End, string Text, string? En, IReadOnlyList<Word>? Words = null)
    {
        public IReadOnlyList<Word> TimedWords()
        {
            if (Words is { Count: > 0 }) return Words;
            var parts = Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var step = Math.Max(0.01, End - Start) / Math.Max(1, parts.Length);
            return parts.Select((w, i) => new Word(Start + i * step, Start + (i + 1) * step, w)).ToList();
        }
    }

    public static List<Line> Lines(string? transcriptJson)
    {
        if (string.IsNullOrWhiteSpace(transcriptJson)) return new();
        JsonNode? root;
        try { root = JsonNode.Parse(transcriptJson); } catch (System.Text.Json.JsonException) { return new(); }
        if (root?["segments"] is not JsonArray segs) return new();
        static double N(JsonNode? n, double fallback) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : fallback;
        var lines = new List<Line>();
        foreach (var s in segs.OfType<JsonObject>())
        {
            var text = s["text"]?.ToString().Trim();
            if (string.IsNullOrEmpty(text)) continue;
            var start = N(s["start"], 0);
            var end = N(s["end"], start);
            List<Word>? words = null;
            if (s["words"] is JsonArray wa)
                words = wa.OfType<JsonArray>().Where(w => w.Count >= 3 && !string.IsNullOrWhiteSpace(w[2]?.ToString()))
                    .Select(w => new Word(N(w[0], start), N(w[1], end), w[2]!.ToString().Trim())).ToList();
            lines.Add(new Line(start, end, text, s["en"]?.ToString().Trim() is { Length: > 0 } en ? en : null, words));
        }
        return lines;
    }

    private static int SceneAt(double t, IReadOnlyList<(double Start, double End)> scenes)
    {
        for (var k = 0; k < scenes.Count; k++)
            if (t >= scenes[k].Start && t < scenes[k].End) return k;
        // before the first / after the last cut: the nearest scene
        return scenes.Count == 0 ? -1 : Enumerable.Range(0, scenes.Count).MinBy(k => Math.Min(Math.Abs(t - scenes[k].Start), Math.Abs(t - scenes[k].End)));
    }

    /// <summary>
    /// Spreads the narration over the scenes. A sentence normally stays whole, in the scene holding its middle (read
    /// once, with its natural intonation). When a scene would then hold more speech than one clip can carry, its
    /// sentences that cross a cut are split word by word, each word going to the scene where it was actually said.
    /// Returns (Vietnamese, English meaning) per scene, in the order given; a split sentence's meaning goes to every
    /// scene that has part of it.
    /// </summary>
    public static List<(string? Text, string? En)> Assign(IReadOnlyList<Line> lines, IReadOnlyList<(double Start, double End)> scenes)
    {
        if (scenes.Count == 0) return new();
        var owner = lines.Select(l => SceneAt((l.Start + l.End) / 2, scenes)).ToList();
        var split = new bool[lines.Count];
        double Load(int k)
        {
            double sum = 0;
            for (var i = 0; i < lines.Count; i++)
            {
                if (!split[i]) { if (owner[i] == k) sum += lines[i].End - lines[i].Start; continue; }
                sum += lines[i].TimedWords().Where(w => SceneAt((w.Start + w.End) / 2, scenes) == k).Sum(w => w.End - w.Start);
            }
            return sum;
        }
        bool Crosses(int i) => lines[i].TimedWords().Select(w => SceneAt((w.Start + w.End) / 2, scenes)).Distinct().Count() > 1;

        for (var guard = 0; guard < lines.Count; guard++)
        {
            var over = Enumerable.Range(0, scenes.Count).Where(k => Load(k) > MaxSceneSpeechSeconds).ToList();
            var candidate = Enumerable.Range(0, lines.Count)
                .Where(i => !split[i] && over.Contains(owner[i]) && Crosses(i))
                .OrderByDescending(i => lines[i].End - lines[i].Start).Cast<int?>().FirstOrDefault();
            if (candidate is not { } c) break;
            split[c] = true;
        }

        var pieces = scenes.Select(_ => new List<(double T, string Text)>()).ToList();
        var meanings = scenes.Select(_ => new List<string>()).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var parts = split[i]
                ? l.TimedWords().GroupBy(w => SceneAt((w.Start + w.End) / 2, scenes))
                    .Select(g => (Scene: g.Key, T: g.First().Start, Text: string.Join(" ", g.Select(w => w.Text))))
                : new[] { (Scene: owner[i], T: l.Start, Text: l.Text) }.AsEnumerable();
            foreach (var part in parts)
            {
                if (part.Scene < 0) continue;
                pieces[part.Scene].Add((part.T, part.Text));
                if (l.En != null && !meanings[part.Scene].Contains(l.En)) meanings[part.Scene].Add(l.En);
            }
        }
        return pieces.Select((p, k) =>
        {
            var text = p.Count == 0 ? null : Cap(string.Join(" ", p.OrderBy(x => x.T).Select(x => x.Text)), 2000);
            var lineCount = lines.Where((l, i) => split[i] ? l.TimedWords().Any(w => SceneAt((w.Start + w.End) / 2, scenes) == k) : owner[i] == k).Count();
            var en = text == null || meanings[k].Count == 0 || meanings[k].Count != lineCount ? null : Cap(string.Join(" ", meanings[k]), 2000);
            return (text, en);
        }).ToList();
    }

    /// <summary>The text the prompt quotes: the English meaning, or the user's own words once they edited them.</summary>
    public static string? PromptText(string? narration, string? en, bool edited) =>
        string.IsNullOrWhiteSpace(narration) ? null : edited || string.IsNullOrWhiteSpace(en) ? narration.Trim() : en.Trim();

    public static string Key(string text, string voice, string style) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{voice}|{style}|{text.Trim()}")))[..32].ToLowerInvariant();

    // ───────────── the reference speaker ─────────────

    public sealed record Speaker(double? PitchHz, double? SyllablesPerSecond, double? LoudnessStdDb);

    public static Speaker? SpeakerOf(string? transcriptJson)
    {
        if (string.IsNullOrWhiteSpace(transcriptJson)) return null;
        JsonNode? root;
        try { root = JsonNode.Parse(transcriptJson); } catch (System.Text.Json.JsonException) { return null; }
        if (root?["voice"] is not JsonObject v) return null;
        static double? N(JsonObject o, string k) => o[k] is JsonValue x && x.TryGetValue<double>(out var d) ? d : null;
        var s = new Speaker(N(v, "pitch_hz"), N(v, "syllables_per_second"), N(v, "loudness_std_db"));
        return s.PitchHz == null && s.SyllablesPerSecond == null ? null : s;
    }

    /// <summary>Under ~165 Hz most voices are men's; the measure is a hint (music under the voice blurs it).</summary>
    public static string? GenderOf(Speaker? s) => s?.PitchHz is { } f ? (f < 165 ? "male" : "female") : null;

    private static string Pace(Speaker? s) => s?.SyllablesPerSecond switch
    {
        null => "unhurried",
        < 2.6 => "slowly, with long pauses",
        < 3.6 => "slowly and calmly",
        < 4.8 => "at a natural pace",
        _ => "briskly"
    };

    private static string PaceVi(double sps) => sps switch
    {
        < 2.6 => "đọc rất chậm",
        < 3.6 => "đọc chậm",
        < 4.8 => "đọc vừa",
        _ => "đọc nhanh"
    };

    /// <summary>"Giọng nam trầm (~112 Hz), đọc chậm (3.1 tiếng/giây), giọng đều" — what was measured, for the page.</summary>
    public static string? Describe(Speaker? s)
    {
        if (s == null) return null;
        var parts = new List<string>();
        if (s.PitchHz is { } f)
        {
            var g = f < 165 ? "nam" : "nữ";
            var tone = f < 165 ? (f < 110 ? "rất trầm" : f < 135 ? "trầm" : "trung") : (f < 200 ? "trầm" : f < 250 ? "trung" : "cao");
            parts.Add($"giọng {g} {tone} (~{f.ToString("0", Inv)} Hz)");
        }
        if (s.SyllablesPerSecond is { } r) parts.Add($"{PaceVi(r)} ({r.ToString("0.0", Inv)} tiếng/giây)");
        if (s.LoudnessStdDb is { } l) parts.Add(l > 7 ? "lên xuống nhiều, diễn cảm" : l < 4 ? "giọng đều" : "lên xuống vừa phải");
        return parts.Count == 0 ? null : char.ToUpper(parts[0][0]) + string.Join(", ", parts)[1..];
    }

    /// <summary>
    /// A narrator voice and English acting direction that sound like the reference speaker within the theme: same
    /// gender and register, same pace, same liveliness, the theme's mood (nostalgic storyteller for the childhood
    /// channel, soft and sweet for the beauty channel).
    /// </summary>
    public static (string Voice, string Style) Suggest(Speaker? s, string topic)
    {
        var childhood = topic == AiStudioTopics.VietnamChildhood;
        var gender = GenderOf(s) ?? (childhood ? "male" : "female");
        var pitch = s?.PitchHz;
        string voice = gender == "male"
            ? pitch is < 115 ? "gemini:Algenib" : childhood ? "gemini:Charon" : "gemini:Achird"
            : childhood ? (pitch is < 200 ? "gemini:Gacrux" : "gemini:Vindemiatrix") : (pitch is > 250 ? "gemini:Leda" : "gemini:Aoede");
        var who = gender == "male"
            ? pitch is < 115 ? "a deep, slightly husky male storyteller" : pitch is < 140 ? "a warm, low male storyteller" : "a gentle male storyteller"
            : pitch is < 200 ? "a mature, warm female storyteller" : "a soft, gentle young female voice";
        var lively = s?.LoudnessStdDb switch
        {
            > 7 => "expressive and emotional, letting the voice rise and fall",
            < 4 => "even and restrained",
            _ => "quietly expressive"
        };
        var mood = childhood
            ? "nostalgic, tender and a little sad, as if remembering a poor childhood in the Vietnamese countryside"
            : topic == AiStudioTopics.AsianBeauty ? "soft, sweet and intimate" : "natural and sincere";
        var style = $"Speak Vietnamese like {who}, {Pace(s)}, {lively}; {mood}; natural pauses between sentences, never like an announcer.";
        return (voice, style);
    }

    /// <summary>The voice options shown on the page: the studio's prebuilt Gemini voices.</summary>
    /// <summary>A prebuilt voice or one designed in the studio's "Giọng đọc" tab ("gemini:voice_…").</summary>
    public static bool IsVoice(string? voice) => StudioVoices.IsKnown(voice);

    private static string Cap(string s, int max) => s.Length > max ? s[..max] : s;
}

/// <summary>Reads narration aloud (the studio's Gemini TTS voices); an interface so the tests can stand in for the paid call.</summary>
public interface IAiStudioNarrator
{
    decimal PricePerSecond { get; }

    /// <summary>One take as compact AAC, its length, whether it was reused for free, and how it was delivered.</summary>
    Task<(byte[] Audio, double Seconds, bool FromCache, string? Delivery)> SpeakAsync(Guid projectId, string text, string voice, string? style,
        string? fallbackVoice, CancellationToken ct, string? speed = null);
}

/// <summary>The narrator through the studio's existing <see cref="StudioVoices"/> (same Gemini key, cache and price).</summary>
public sealed class StudioVoicesNarrator : IAiStudioNarrator
{
    private readonly StudioVoices _voices;

    public StudioVoicesNarrator(StudioVoices voices) => _voices = voices;

    public decimal PricePerSecond => _voices.PricePerSecond;

    public Task<(byte[] Audio, double Seconds, bool FromCache, string? Delivery)> SpeakAsync(Guid projectId, string text, string voice, string? style,
        string? fallbackVoice, CancellationToken ct, string? speed = null) =>
        _voices.SpeakAsync(projectId, text, voice, speed, ct, style, fallbackVoice);
}
