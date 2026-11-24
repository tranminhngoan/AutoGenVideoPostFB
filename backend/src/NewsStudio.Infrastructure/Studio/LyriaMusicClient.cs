using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Infrastructure.Common;

namespace NewsStudio.Infrastructure.Studio;

/// <summary>A ready-made brief for an original background track (Vietnamese name and mood, English prompt for the model).</summary>
public sealed record MusicBrief(string Id, string Name, string Mood, string Prompt);

/// <summary>
/// Original background music composed by Google Lyria through the Gemini API (the paid Gemini key), so the music store
/// fills itself without downloading anything — and since the tracks are made for this account, Facebook has nothing to
/// match them against (no copyright claims). POST {base}/interactions { model, input, response_format: audio } →
/// steps[].content[] { type: "audio", data: base64 MP3 }. lyria-3.5 writes a whole song (a couple of minutes, which the
/// compose step loops / cuts to the video); every track carries Google's inaudible SynthID watermark.
/// </summary>
public class LyriaMusicClient
{
    public const string DefaultModel = "lyria-3.5";
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/interactions";

    /// <summary>Countryside / nostalgia briefs matching the moods of the script library.</summary>
    public static readonly IReadOnlyList<MusicBrief> Briefs = new[]
    {
        new MusicBrief("que-buon-nghen", "Sáo trúc đàn tranh – buồn nghẹn", "buồn nghẹn, nước mắt, nhớ mẹ, quê hương, sáo trúc, đàn tranh",
            "Slow, deeply sad and tender Vietnamese countryside instrumental, about 66 BPM, in a minor pentatonic mode. A solo Vietnamese bamboo flute " +
            "(sao truc) sings the melody over gentle Vietnamese zither (dan tranh) arpeggios and very soft warm strings, evoking a poor mother cooking " +
            "by a wood stove on a rainy evening in a 1990s village. Soft intro, an emotional swelling middle, a quiet fading ending. Instrumental only, no vocals, no drums."),
        new MusicBrief("que-da-diet", "Đàn bầu piano – da diết nhớ cha mẹ", "da diết, nhớ cha mẹ, day dứt, hoài niệm, đàn bầu",
            "Slow, longing Vietnamese instrumental, about 62 BPM. The Vietnamese monochord (dan bau) plays a bending, crying melody over soft sustained " +
            "piano chords and a low cello line, like a grown child remembering late parents in their old countryside home. Intimate and heartfelt, " +
            "gradual build, gentle ending. Instrumental only, no vocals, no drums."),
        new MusicBrief("que-hoai-niem", "Guitar mộc sáo – hoài niệm ấm áp", "hoài niệm, ấm áp, tuổi thơ, quê hương, guitar mộc",
            "Warm, nostalgic acoustic instrumental, about 78 BPM. Fingerpicked nylon-string acoustic guitar with a soft bamboo flute melody and light " +
            "warm pads, like memories of a 1990s Vietnamese village childhood: rice fields at sunset, kids playing barefoot. Bittersweet but comforting, " +
            "simple and sincere. Instrumental only, no vocals, very light or no percussion."),
        new MusicBrief("que-yen-binh", "Cánh đồng buổi sáng – yên bình", "yên bình, đồng quê, cánh đồng, nhẹ nhàng, sáo trúc",
            "Peaceful, airy Vietnamese countryside morning instrumental, about 70 BPM. Breathy bamboo flute phrases, gentle zither (dan tranh) plucks, " +
            "soft ambient pads and a calm, open feel of green rice fields, a river and morning mist. Serene, unhurried, loop-friendly. Instrumental only, no vocals, no drums."),
        new MusicBrief("que-tuoi-tho-vui", "Tuổi thơ hồn nhiên – vui tươi", "vui tươi, hóm hỉnh, tuổi thơ, hồn nhiên, ngây ngô",
            "Light, playful and innocent childhood instrumental with a Vietnamese folk flavour, about 104 BPM. Bouncy plucked zither (dan tranh) and " +
            "bamboo flute, pizzicato strings and light hand percussion (wood block, small drum), like village kids flying kites and stealing fruit. " +
            "Cheerful, a little mischievous, warm. Instrumental only, no vocals."),
        new MusicBrief("que-piano-nghen-ngao", "Piano mưa – nghẹn ngào xót xa", "nghẹn ngào, xót xa, buồn, mưa, piano",
            "Slow, emotional solo piano with soft strings, about 60 BPM, minor key. Simple, tender melody with space between notes, like rain on a tin " +
            "roof and a mother's quiet sacrifice. Cinematic but intimate, building gently and ending softly. Instrumental only, no vocals, no drums."),
        new MusicBrief("mien-tay-don-ca", "Đờn ca tài tử – miền Tây sông nước", "miền Tây, sông nước, mộc mạc, chân chất, hoài niệm, đờn ca tài tử",
            "Gentle instrumental in the style of Southern Vietnamese don ca tai tu (Mekong Delta chamber folk), about 70 BPM. A slide-fretted " +
            "acoustic guitar (guitar phim lom) and the dan kim moon lute trade warm, sliding phrases over a soft dan tranh zither and a light, " +
            "unhurried rhythm, evoking canals, coconut trees, sampans and a peaceful afternoon in the Mekong Delta. Rustic, friendly, nostalgic, " +
            "not sad. Instrumental only, no vocals, no drums."),
        new MusicBrief("mien-tay-guitar", "Guitar phím lõm – chiều quê miền Tây", "miền Tây, ấm áp, thong thả, quê nhà, guitar phím lõm",
            "Relaxed, warm Southern Vietnamese countryside instrumental, about 76 BPM. A slide-fretted acoustic guitar (guitar phim lom) plays a " +
            "simple, swaying melody over soft fingerpicked guitar and a bamboo flute answering now and then, like a late afternoon on a Mekong " +
            "Delta river bank with family. Cheerful-gentle and homely, not melancholic. Instrumental only, no vocals, light or no percussion."),
        new MusicBrief("que-thuong-que", "Đàn bầu sáo – thương quê", "xót xa, thương quê, mộc mạc, quê hương, đàn bầu",
            "Bittersweet Vietnamese folk instrumental, about 72 BPM. Monochord (dan bau) and bamboo flute in a gentle call-and-response over acoustic " +
            "guitar and soft strings, evoking a poor but loving rural homeland, thatched houses, bamboo hedges and cooking smoke at dusk. Sincere, " +
            "rustic, moving. Instrumental only, no vocals, no drums."),
    };

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LyriaMusicClient> _logger;

    public LyriaMusicClient(HttpClient http, IConfiguration configuration, ILogger<LyriaMusicClient> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsConfigured => GeminiKeys.HasAny(_configuration);

    public string Model => string.IsNullOrWhiteSpace(_configuration["Studio:MusicModel"]) ? DefaultModel : _configuration["Studio:MusicModel"]!.Trim();

    /// <summary>Price of one song (Google list price for lyria-3.5: $0.08).</summary>
    public decimal PricePerSong =>
        decimal.TryParse(_configuration["Studio:MusicPriceUsd"], NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p >= 0 ? p : 0.08m;

    /// <summary>A free-text wish ("nhạc buồn sáo trúc…") turned into an instrumental brief for the model.</summary>
    public static string CustomPrompt(string wish) =>
        "Original background instrumental for a short nostalgic video about the Vietnamese countryside. The user's wish (in Vietnamese): \"" +
        wish.Trim() + "\". Follow it for mood, tempo and instruments (Vietnamese folk instruments such as bamboo flute, dan tranh zither or dan bau " +
        "monochord are welcome). A complete piece with intro, development and a soft ending. Instrumental only, no vocals.";

    public static JsonObject BuildRequest(string model, string prompt) => new()
    {
        ["model"] = model,
        ["input"] = prompt,
        ["response_format"] = new JsonObject { ["type"] = "audio" }
    };

    /// <summary>The first base64 audio of an Interactions answer, with a file extension guessed from its bytes.</summary>
    public static (byte[] Audio, string Extension)? ParseAudio(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var data = FindAudio(doc.RootElement);
        if (data == null) return null;
        var bytes = Convert.FromBase64String(data);
        var ext = bytes.Length > 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" ? ".wav"
            : bytes.Length > 8 && Encoding.ASCII.GetString(bytes, 4, 4) == "ftyp" ? ".m4a"
            : bytes.Length > 4 && Encoding.ASCII.GetString(bytes, 0, 4) == "OggS" ? ".ogg"
            : ".mp3";
        return (bytes, ext);
    }

    private static string? FindAudio(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                string? type = null, mime = null, data = null;
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.String) continue;
                    if (p.NameEquals("type")) type = p.Value.GetString();
                    else if (p.NameEquals("mime_type") || p.NameEquals("mimeType")) mime = p.Value.GetString();
                    else if (p.NameEquals("data")) data = p.Value.GetString();
                }
                if (data is { Length: > 100 } && (type == "audio" || mime?.StartsWith("audio", StringComparison.OrdinalIgnoreCase) == true)) return data;
                foreach (var p in e.EnumerateObject())
                    if (FindAudio(p.Value) is { } inner) return inner;
                return null;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray())
                    if (FindAudio(x) is { } inner) return inner;
                return null;
            default:
                return null;
        }
    }

    /// <summary>Composes one track; tries the paid key last (no free tier for Lyria).</summary>
    public async Task<(byte[] Audio, string Extension, string Model)> GenerateAsync(string prompt, CancellationToken ct)
    {
        var model = Model;
        var body = BuildRequest(model, prompt).ToJsonString();
        string? last = null;
        foreach (var key in GeminiKeys.InOrder(_configuration, model))
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("x-goog-api-key", key.Value);
            req.Headers.Add("Api-Revision", "2026-05-20");
            using var res = await _http.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode)
            {
                if (ParseAudio(json) is { } audio && audio.Audio.Length > 10_000)
                {
                    _logger.LogInformation("Lyria composed {Bytes} bytes with {Model} ({KeyType} key)", audio.Audio.Length, model, key.Label);
                    return (audio.Audio, audio.Extension, model);
                }
                last = "Google không trả về file nhạc.";
                continue;
            }
            last = $"HTTP {(int)res.StatusCode}: {ErrorOf(json)}";
            _logger.LogWarning("Lyria {Model} with the {KeyType} key: {Error}", model, key.Label, last);
        }
        throw new InvalidOperationException($"Không sáng tác được nhạc bằng {model} — {last ?? "chưa có khoá Gemini"}");
    }

    private static string ErrorOf(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var m) ? m.GetString() ?? err.GetRawText() : err.ToString();
        }
        catch (JsonException) { }
        return body.Length > 300 ? body[..300] : body;
    }
}
