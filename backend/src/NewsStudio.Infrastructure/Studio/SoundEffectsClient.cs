using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NewsStudio.Infrastructure.Studio;

/// <summary>
/// Sound effects for a shot: ElevenLabs Sound Effects v2 through the Vercel AI Gateway — the same key and credit as the
/// Seedance clips, no other account. It turns the shot's sound notes (the script's own "âm thanh" lines, translated to
/// English when the script is imported) into an effects track exactly as long as the clip: rain on banana leaves, a
/// crackling wood stove, frogs at dusk, footsteps on a dirt road… The AI SDK sends sound effects through the gateway's
/// speech endpoint: POST {base}/speech-model with { text, outputFormat, providerOptions.elevenlabs.durationSeconds },
/// answered with { audio: base64 }. Billed per second of audio (ElevenLabs list price ≈ $0.12 a minute).
/// </summary>
public class SoundEffectsClient
{
    public const string Model = "elevenlabs/sound-effects-v2";
    public const double MinSeconds = 0.5;
    public const double MaxSeconds = 22;
    private const string DefaultBaseUrl = "https://ai-gateway.vercel.sh/v4/ai";

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SoundEffectsClient> _logger;

    public SoundEffectsClient(HttpClient http, IConfiguration configuration, ILogger<SoundEffectsClient> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Same key as the video clips (AI_GATEWAY_API_KEY).</summary>
    public bool IsConfigured => SeedanceVideoClient.ApiKey(_configuration) != null;

    private string BaseUrl => string.IsNullOrWhiteSpace(_configuration["Ai:Gateway:BaseUrl"]) ? DefaultBaseUrl : _configuration["Ai:Gateway:BaseUrl"]!.Trim().TrimEnd('/');

    public decimal PricePerSecond =>
        decimal.TryParse(_configuration["Studio:SfxPricePerSecondUsd"], NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p >= 0 ? p : 0.002m;

    public decimal EstimateCost(double seconds) => Math.Round((decimal)Math.Clamp(seconds, MinSeconds, MaxSeconds) * PricePerSecond, 4);

    /// <summary>
    /// What ElevenLabs is asked to make: the shot's sound notes; with none, a quiet ambience taken from the start of
    /// the picture's description. Sound effects only — voices are dubbed by TTS and the music is the user's own.
    /// </summary>
    public static string BuildPrompt(string? sound, string action)
    {
        var text = sound?.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(text))
        {
            var scene = (action ?? "").Trim();
            if (scene.Length > 160) scene = scene[..160];
            text = scene.Length == 0 ? "soft natural ambience" : $"soft natural ambience for this scene: {scene}";
        }
        return text.Length > 450 ? text[..450] : text;
    }

    public static JsonObject BuildRequest(string prompt, double seconds) => new()
    {
        ["text"] = prompt,
        ["outputFormat"] = "mp3",
        ["providerOptions"] = new JsonObject
        {
            ["elevenlabs"] = new JsonObject { ["durationSeconds"] = Math.Round(Math.Clamp(seconds, MinSeconds, MaxSeconds), 2) }
        }
    };

    /// <summary>The effects track (MP3 bytes) for one clip of <paramref name="seconds"/>.</summary>
    public async Task<byte[]> GenerateAsync(string prompt, double seconds, CancellationToken ct)
    {
        var key = SeedanceVideoClient.ApiKey(_configuration)
                  ?? throw new InvalidOperationException("Chưa có khoá Vercel AI Gateway (AI_GATEWAY_API_KEY) — chưa tạo được hiệu ứng âm thanh.");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/speech-model")
        {
            Content = new StringContent(BuildRequest(prompt, seconds).ToJsonString(), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Headers.Add("ai-gateway-protocol-version", "0.0.1");
        req.Headers.Add("ai-gateway-auth-method", "api-key");
        req.Headers.Add("ai-speech-model-specification-version", "4");
        req.Headers.Add("ai-model-id", Model);
        using var res = await _http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            var message = ErrorOf(text);
            var code = (int)res.StatusCode;
            _logger.LogWarning("Sound effects HTTP {Status}: {Message}", code, message);
            if (code is 401 or 402 or 403 || SeedanceVideoClient.IsAccountProblem(message))
                throw new InvalidOperationException($"Tài khoản Vercel AI Gateway không dùng được cho hiệu ứng âm thanh (HTTP {code}: {message}).");
            throw new InvalidOperationException($"Không tạo được hiệu ứng âm thanh (HTTP {code}): {message}");
        }
        var audio = ParseAudio(text) ?? throw new InvalidOperationException("Vercel không trả về âm thanh.");
        if (audio.Length < 512) throw new InvalidOperationException("File âm thanh trả về bị hỏng.");
        return audio;
    }

    /// <summary>The base64 "audio" of a speech-model answer.</summary>
    public static byte[]? ParseAudio(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("audio", out var a) && a.ValueKind == JsonValueKind.String && a.GetString() is { Length: > 0 } b64
            ? Convert.FromBase64String(b64)
            : null;
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
