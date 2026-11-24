using System.Globalization;
using Microsoft.Extensions.Configuration;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Enums;
using NewsStudio.Infrastructure.Rendering;

namespace NewsStudio.Infrastructure.Studio;

/// <summary>A Gemini TTS voice offered to the characters of a studio video.</summary>
public sealed record StudioVoice(string Id, string Name, string Gender, string Description);

/// <summary>
/// Dialogue voices: each line is read by Gemini TTS in the voice given to its speaker. Seedance 1.0 makes silent video,
/// so these voices (plus the music) are the whole soundtrack. Only the line itself is sent — an acting direction in
/// front of it was sometimes read aloud by the model (see <see cref="GeminiTtsService"/>).
/// </summary>
public class StudioVoices
{
    public const string NarratorName = "Lời dẫn";

    public static readonly IReadOnlyList<StudioVoice> All = new[]
    {
        new StudioVoice("gemini:Charon", "Charon", "male", "Nam, trầm ấm, điềm đạm"),
        new StudioVoice("gemini:Orus", "Orus", "male", "Nam, chắc giọng, nghiêm"),
        new StudioVoice("gemini:Algenib", "Algenib", "male", "Nam, khàn, từng trải"),
        new StudioVoice("gemini:Sadaltager", "Sadaltager", "male", "Nam, chững chạc, hiểu biết"),
        new StudioVoice("gemini:Puck", "Puck", "male", "Nam, trẻ trung, vui"),
        new StudioVoice("gemini:Fenrir", "Fenrir", "male", "Nam, sôi nổi, hay kích động"),
        new StudioVoice("gemini:Achird", "Achird", "male", "Nam, thân thiện"),
        new StudioVoice("gemini:Iapetus", "Iapetus", "male", "Nam, rõ ràng"),
        new StudioVoice("gemini:Umbriel", "Umbriel", "male", "Nam, thoải mái"),
        new StudioVoice("gemini:Enceladus", "Enceladus", "male", "Nam, nhẹ, thì thầm"),
        new StudioVoice("gemini:Kore", "Kore", "female", "Nữ, chắc, rõ ràng"),
        new StudioVoice("gemini:Gacrux", "Gacrux", "female", "Nữ, đứng tuổi, trưởng thành"),
        new StudioVoice("gemini:Pulcherrima", "Pulcherrima", "female", "Nữ, thẳng thắn, đanh"),
        new StudioVoice("gemini:Sulafat", "Sulafat", "female", "Nữ, ấm áp"),
        new StudioVoice("gemini:Aoede", "Aoede", "female", "Nữ, nhẹ nhàng"),
        new StudioVoice("gemini:Leda", "Leda", "female", "Nữ, trẻ trung"),
        new StudioVoice("gemini:Zephyr", "Zephyr", "female", "Nữ, tươi sáng"),
        new StudioVoice("gemini:Despina", "Despina", "female", "Nữ, mượt mà"),
        new StudioVoice("gemini:Vindemiatrix", "Vindemiatrix", "female", "Nữ, dịu dàng"),
        new StudioVoice("gemini:Achernar", "Achernar", "female", "Nữ, mềm, nhỏ nhẹ"),
    };

    public const string DefaultNarratorVoice = "gemini:Charon";

    public static bool IsKnown(string? voice) => All.Any(v => v.Id == voice) || IsDesigned(voice);

    /// <summary>A voice designed for this account ("gemini:voice_…").</summary>
    public static bool IsDesigned(string? voice) => voice?.StartsWith("gemini:voice_", StringComparison.Ordinal) == true;

    /// <summary>The prebuilt voice a line falls back to when its designed voice can't be used.</summary>
    public static string FallbackFor(string? gender) => gender == "female" ? "gemini:Sulafat" : "gemini:Charon";

    /// <summary>"-6%", "+4%"… within -20 % … +30 %; anything else reads at normal speed.</summary>
    public static string NormalizeSpeed(string? speed)
    {
        var m = System.Text.RegularExpressions.Regex.Match(speed?.Trim() ?? "", @"^([+-]?)(\d{1,2})%?$");
        if (!m.Success) return "+0%";
        var value = int.Parse(m.Groups[2].Value) * (m.Groups[1].Value == "-" ? -1 : 1);
        value = Math.Clamp(value, -20, 30);
        return (value < 0 ? "" : "+") + value + "%";
    }

    /// <summary>A voice nobody in the cast has yet, of the right gender (falls back to any of that gender).</summary>
    public static string Pick(string? gender, IEnumerable<string> taken)
    {
        var g = gender?.Trim().ToLowerInvariant() is "female" or "f" or "woman" or "nữ" or "nu" ? "female" : "male";
        var used = taken.ToHashSet();
        return All.FirstOrDefault(v => v.Gender == g && !used.Contains(v.Id))?.Id ?? All.First(v => v.Gender == g).Id;
    }

    public static IEnumerable<StudioVoiceDto> Dtos() => All.Select(v => new StudioVoiceDto { Id = v.Id, Name = v.Name, Gender = v.Gender, Description = v.Description });

    private readonly GeminiTtsService _tts;
    private readonly StudioMedia _media;
    private readonly IConfiguration _configuration;

    public StudioVoices(GeminiTtsService tts, StudioMedia media, IConfiguration configuration)
    {
        _tts = tts;
        _media = media;
        _configuration = configuration;
    }

    /// <summary>
    /// Designs a voice from a description (Gemini voice design) and returns its id ("gemini:voice_…") with Google's
    /// sample as compact AAC for listening.
    /// </summary>
    public async Task<(string Id, byte[]? SampleAac)> DesignAsync(string name, string gender, string description, CancellationToken ct)
    {
        var (voiceId, sample) = await _tts.DesignVoiceAsync(name, gender, description, ct);
        byte[]? aac = null;
        if (sample is { Length: > 1000 })
        {
            var dir = Path.Combine(Path.GetTempPath(), "voice_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var wav = Path.Combine(dir, "sample.wav");
                var isWav = sample.Length > 12 && System.Text.Encoding.ASCII.GetString(sample, 0, 4) == "RIFF";
                await File.WriteAllBytesAsync(wav, isWav ? sample : PcmToWav(sample, 24000), ct);
                aac = await _media.EncodeVoiceAsync(wav, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { aac = null; }
            finally { try { Directory.Delete(dir, true); } catch { /* best effort */ } }
        }
        return ("gemini:" + voiceId, aac);
    }

    private static byte[] PcmToWav(byte[] pcm, int rate)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + pcm.Length); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(pcm.Length); w.Write(pcm);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Longest a take of these words can plausibly last (very slow, with pauses): ~1.8 words a second + 4 s.</summary>
    public static double MaxPlausibleSeconds(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length / 1.8 + 4;

    /// <summary>Gemini TTS ≈ $10 per million audio tokens at 25 tokens a second, plus the short text: ~$0.0003 a second.</summary>
    public decimal PricePerSecond =>
        decimal.TryParse(_configuration["Studio:VoicePricePerSecondUsd"], NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p >= 0 ? p : 0.0003m;

    /// <summary>
    /// One spoken take as a compact AAC file (kept in the database: a few KB a second) and its length. A
    /// <paramref name="style"/> (English acting direction) makes it performed rather than read; a take far longer
    /// than its words could ever need (the direction spoken aloud) is read again without it.
    /// </summary>
    public async Task<(byte[] Audio, double Seconds, bool FromCache, string? Delivery)> SpeakAsync(Guid videoId, string line, string voice, string? speed,
        CancellationToken ct, string? style = null, string? fallbackVoice = null)
    {
        var request = new TtsSynthesisRequest
        {
            ProjectId = videoId,
            Text = line.Trim(),
            Voice = IsKnown(voice) ? voice : DefaultNarratorVoice,
            Speed = NormalizeSpeed(speed),
            Provider = TtsProviderType.GoogleTts,
            Style = string.IsNullOrWhiteSpace(style) ? null : style.Trim(),
            FallbackVoice = fallbackVoice
        };
        var result = await _tts.SynthesizeSpeechWithSubtitlesAsync(request, ct);
        string? delivery = request.Style == null ? null
            : result.Styled ? "diễn cảm" : "giọng thường — model diễn cảm lỗi: " + result.StyleError;
        if (request.Style != null && result.AudioDurationSeconds > MaxPlausibleSeconds(line))
        {
            try { File.Delete(result.AudioFilePath); } catch { /* best effort */ }
            request.Style = null;
            result = await _tts.SynthesizeSpeechWithSubtitlesAsync(request, ct);
            delivery = "giọng thường — bản diễn cảm dài bất thường";
        }
        try
        {
            var aac = await _media.EncodeVoiceAsync(result.AudioFilePath, ct);
            return (aac, Math.Round(result.AudioDurationSeconds, 2), result.FromCache, delivery);
        }
        finally
        {
            try { File.Delete(result.AudioFilePath); } catch { /* best effort */ }
        }
    }
}
