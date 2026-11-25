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

    public sealed record Line(double Start, double End, string Text, string? En);

    public static List<Line> Lines(string? transcriptJson)
    {
        if (string.IsNullOrWhiteSpace(transcriptJson)) return new();
        JsonNode? root;
        try { root = JsonNode.Parse(transcriptJson); } catch (System.Text.Json.JsonException) { return new(); }
        if (root?["segments"] is not JsonArray segs) return new();
        var lines = new List<Line>();
        foreach (var s in segs.OfType<JsonObject>())
        {
            var text = s["text"]?.ToString().Trim();
            if (string.IsNullOrEmpty(text)) continue;
            var start = s["start"] is JsonValue a && a.TryGetValue<double>(out var x) ? x : 0;
            var end = s["end"] is JsonValue b && b.TryGetValue<double>(out var y) ? y : start;
            lines.Add(new Line(start, end, text, s["en"]?.ToString().Trim() is { Length: > 0 } en ? en : null));
        }
        return lines;
    }

    /// <summary>
    /// Each spoken line belongs to ONE scene — the one holding its middle — so a sentence across a cut is read once.
    /// Returns (Vietnamese, English) per scene start/end, in the order given.
    /// </summary>
    public static List<(string? Text, string? En)> Assign(IReadOnlyList<Line> lines, IReadOnlyList<(double Start, double End)> scenes)
    {
        var result = scenes.Select(_ => (Text: new List<string>(), En: new List<string>())).ToList();
        foreach (var l in lines)
        {
            var mid = (l.Start + l.End) / 2;
            var i = -1;
            for (var k = 0; k < scenes.Count; k++)
                if (mid >= scenes[k].Start && mid < scenes[k].End) { i = k; break; }
            if (i < 0 && scenes.Count > 0) // before the first / after the last cut: the nearest scene
                i = Enumerable.Range(0, scenes.Count).MinBy(k => Math.Min(Math.Abs(mid - scenes[k].Start), Math.Abs(mid - scenes[k].End)));
            if (i < 0) continue;
            result[i].Text.Add(l.Text);
            if (l.En != null) result[i].En.Add(l.En);
        }
        return result.Select(r => (
            r.Text.Count == 0 ? null : Cap(string.Join(" ", r.Text), 2000),
            r.En.Count == 0 || r.En.Count != r.Text.Count ? null : Cap(string.Join(" ", r.En), 2000))).ToList();
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
    public static bool IsVoice(string? voice) => StudioVoices.All.Any(v => v.Id == voice);

    private static string Cap(string s, int max) => s.Length > max ? s[..max] : s;
}

/// <summary>Reads narration aloud (the studio's Gemini TTS voices); an interface so the tests can stand in for the paid call.</summary>
public interface IAiStudioNarrator
{
    decimal PricePerSecond { get; }

    /// <summary>One take as compact AAC, its length, whether it was reused for free, and how it was delivered.</summary>
    Task<(byte[] Audio, double Seconds, bool FromCache, string? Delivery)> SpeakAsync(Guid projectId, string text, string voice, string? style,
        string? fallbackVoice, CancellationToken ct);
}

/// <summary>The narrator through the studio's existing <see cref="StudioVoices"/> (same Gemini key, cache and price).</summary>
public sealed class StudioVoicesNarrator : IAiStudioNarrator
{
    private readonly StudioVoices _voices;

    public StudioVoicesNarrator(StudioVoices voices) => _voices = voices;

    public decimal PricePerSecond => _voices.PricePerSecond;

    public Task<(byte[] Audio, double Seconds, bool FromCache, string? Delivery)> SpeakAsync(Guid projectId, string text, string voice, string? style,
        string? fallbackVoice, CancellationToken ct) =>
        _voices.SpeakAsync(projectId, text, voice, null, ct, style, fallbackVoice);
}
