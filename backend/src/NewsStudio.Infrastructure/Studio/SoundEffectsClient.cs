using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NewsStudio.Infrastructure.Studio;

/// <summary>
/// Sound effects that match the picture: fal.ai's MMAudio v2 (video-to-audio) watches a silent Seedance clip and adds
/// the sounds of what happens in it — wind in the leaves, footsteps, a slammed table, laughter, rain, birds — in sync.
/// Queue API: POST https://queue.fal.run/fal-ai/mmaudio-v2 → status_url / response_url, polled until COMPLETED; the
/// result is the clip with its new soundtrack, whose audio is kept. The clip is passed as a public URL of the app (fal
/// downloads it) or, without one, as a data URI. Key: Ai:Fal:ApiKey / FAL_KEY. Price ≈ $0.001 a second.
/// </summary>
public class SoundEffectsClient
{
    private const string SubmitUrl = "https://queue.fal.run/fal-ai/mmaudio-v2";

    /// <summary>What MMAudio must not add: voices are dubbed by TTS and the music comes from the user's library.</summary>
    public const string NegativePrompt = "speech, talking, human voice, dialogue, narration, singing, music, melody, background music";

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SoundEffectsClient> _logger;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan MaxWait { get; set; } = TimeSpan.FromMinutes(6);

    public SoundEffectsClient(HttpClient http, IConfiguration configuration, ILogger<SoundEffectsClient> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public static string? ApiKey(IConfiguration configuration) =>
        NonEmpty(configuration["Ai:Fal:ApiKey"]) ?? NonEmpty(configuration["FAL_KEY"]) ?? NonEmpty(Environment.GetEnvironmentVariable("FAL_KEY"));

    public bool IsConfigured => ApiKey(_configuration) != null;

    public decimal PricePerSecond =>
        decimal.TryParse(_configuration["Studio:SfxPricePerSecondUsd"], NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p >= 0 ? p : 0.001m;

    public decimal EstimateCost(double seconds) => Math.Round((decimal)Math.Max(1, seconds) * PricePerSecond, 4);

    /// <summary>The app's own public address (fal fetches clips from it): App:PublicBaseUrl, else Railway's public domain.</summary>
    public string? PublicBaseUrl()
    {
        var configured = NonEmpty(_configuration["App:PublicBaseUrl"]);
        if (configured != null) return configured.TrimEnd('/');
        var railway = NonEmpty(_configuration["RAILWAY_PUBLIC_DOMAIN"]) ?? NonEmpty(Environment.GetEnvironmentVariable("RAILWAY_PUBLIC_DOMAIN"));
        return railway == null ? null : "https://" + railway.TrimEnd('/');
    }

    /// <summary>MMAudio's brief for one clip: the planned sound plus what happens on screen.</summary>
    public static string BuildPrompt(string? sound, string action)
    {
        var parts = new[] { sound, action }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim().TrimEnd('.'));
        var text = string.Join(". ", parts);
        if (text.Length == 0) text = "natural ambient sound of the scene";
        return (text.Length > 600 ? text[..600] : text) + ". Realistic sound effects and ambience only.";
    }

    public static JsonObject BuildRequest(string videoUrl, string prompt, double seconds, int? seed)
    {
        var body = new JsonObject
        {
            ["video_url"] = videoUrl,
            ["prompt"] = prompt,
            ["negative_prompt"] = NegativePrompt,
            ["duration"] = Math.Round(Math.Clamp(seconds, 1, 30), 2),
            ["num_steps"] = 25,
            ["cfg_strength"] = 4.5
        };
        if (seed != null) body["seed"] = seed.Value % 65536;
        return body;
    }

    /// <summary>
    /// Sound for one clip, as the video file MMAudio returns (the caller keeps its audio track).
    /// <paramref name="clipUrl"/> is used when given (public URL), otherwise the clip goes inline as a data URI.
    /// </summary>
    public async Task<byte[]> GenerateAsync(string? clipUrl, byte[] clip, string prompt, double seconds, int? seed, CancellationToken ct)
    {
        var key = ApiKey(_configuration) ?? throw new InvalidOperationException("Chưa có khoá fal.ai (biến FAL_KEY trên Railway) — chưa tạo được hiệu ứng âm thanh.");
        var videoUrl = clipUrl ?? "data:video/mp4;base64," + Convert.ToBase64String(clip);

        using var submit = new HttpRequestMessage(HttpMethod.Post, SubmitUrl)
        {
            Content = new StringContent(BuildRequest(videoUrl, prompt, seconds, seed).ToJsonString(), Encoding.UTF8, "application/json")
        };
        submit.Headers.Authorization = new AuthenticationHeaderValue("Key", key);
        string statusUrl, responseUrl;
        using (var res = await _http.SendAsync(submit, ct))
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) throw Failure("gửi yêu cầu", (int)res.StatusCode, text);
            using var doc = JsonDocument.Parse(text);
            statusUrl = Str(doc.RootElement, "status_url") ?? throw new InvalidOperationException("fal.ai không trả về status_url.");
            responseUrl = Str(doc.RootElement, "response_url") ?? throw new InvalidOperationException("fal.ai không trả về response_url.");
        }

        var started = DateTime.UtcNow;
        while (true)
        {
            await Task.Delay(PollInterval, ct);
            using var poll = new HttpRequestMessage(HttpMethod.Get, statusUrl);
            poll.Headers.Authorization = new AuthenticationHeaderValue("Key", key);
            using var res = await _http.SendAsync(poll, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(text);
                var status = Str(doc.RootElement, "status");
                if (status == "COMPLETED")
                {
                    if (Str(doc.RootElement, "error") is { Length: > 0 } err) throw new InvalidOperationException($"fal.ai không tạo được âm thanh: {err}");
                    break;
                }
            }
            else if ((int)res.StatusCode is 401 or 403 or 404) throw Failure("kiểm tra trạng thái", (int)res.StatusCode, text);
            if (DateTime.UtcNow - started > MaxWait) throw new InvalidOperationException("fal.ai tạo âm thanh quá lâu — bấm tạo lại hiệu ứng sau ít phút.");
        }

        using var result = new HttpRequestMessage(HttpMethod.Get, responseUrl);
        result.Headers.Authorization = new AuthenticationHeaderValue("Key", key);
        string fileUrl;
        using (var res = await _http.SendAsync(result, ct))
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) throw Failure("lấy kết quả", (int)res.StatusCode, text);
            fileUrl = ParseResultUrl(text) ?? throw new InvalidOperationException("fal.ai không trả về file âm thanh.");
        }
        using var dl = await _http.GetAsync(fileUrl, ct);
        if (!dl.IsSuccessStatusCode) throw new InvalidOperationException($"Không tải được file âm thanh từ fal.ai (HTTP {(int)dl.StatusCode}).");
        var bytes = await dl.Content.ReadAsByteArrayAsync(ct);
        _logger.LogInformation("MMAudio {Seconds:F1}s in {Elapsed:F0}s", seconds, (DateTime.UtcNow - started).TotalSeconds);
        return bytes;
    }

    /// <summary>The generated file of a result: {"video":{"url":…}} (or "audio" / "audio_file" should the model change).</summary>
    public static string? ParseResultUrl(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var name in new[] { "video", "audio", "audio_file" })
            if (doc.RootElement.TryGetProperty(name, out var f) && f.ValueKind == JsonValueKind.Object && Str(f, "url") is { Length: > 0 } url)
                return url;
        return null;
    }

    private static InvalidOperationException Failure(string step, int status, string body)
    {
        var detail = body.Length > 300 ? body[..300] : body;
        return status is 401 or 403
            ? new InvalidOperationException($"fal.ai từ chối khoá (HTTP {status}) — kiểm tra FAL_KEY và số dư tài khoản fal.ai.")
            : status == 402 || detail.Contains("balance", StringComparison.OrdinalIgnoreCase) || detail.Contains("credit", StringComparison.OrdinalIgnoreCase)
                ? new InvalidOperationException("Tài khoản fal.ai hết tiền — nạp thêm ở fal.ai rồi bấm tạo hiệu ứng lại.")
                : new InvalidOperationException($"fal.ai lỗi khi {step} (HTTP {status}): {detail}");
    }

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
