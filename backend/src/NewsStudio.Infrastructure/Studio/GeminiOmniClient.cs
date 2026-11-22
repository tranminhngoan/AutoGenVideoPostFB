using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Infrastructure.Common;

namespace NewsStudio.Infrastructure.Studio;

public sealed record OmniReference(byte[] Data, string MimeType);

public sealed record OmniClip(byte[] Video, string MimeType, string? InteractionId, int Seconds, decimal CostUsd);

/// <summary>Omni refused the request (safety filter, bad argument…): retrying the same prompt will not help.</summary>
public sealed class OmniRejectedException : InvalidOperationException
{
    public OmniRejectedException(string message) : base(message) { }
}

/// <summary>
/// Gemini Omni video generation (POST /v1beta/interactions, model gemini-omni-1.2-flash): one clip of 3–15 s with its
/// own audio, from a text prompt plus up to 6 reference images (characters / location). Paid tier only — there is no
/// free tier for Omni — so the paid key (Ai:Gemini:ApiKey / GEMINI_API_KEY) is required.
/// Cost = seconds × the per-second price of the resolution (Studio:PricePerSecondUsd).
/// </summary>
public class GeminiOmniClient
{
    public const int MaxReferenceImages = 6;
    public const int MinSeconds = 3;
    public const int MaxSeconds = 15;
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/interactions";

    private static readonly Dictionary<string, decimal> DefaultPrices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["360p"] = 0.023m, ["720p"] = 0.07m, ["1080p"] = 0.104m, ["4k"] = 0.209m
    };

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiOmniClient> _logger;

    public GeminiOmniClient(HttpClient http, IConfiguration configuration, ILogger<GeminiOmniClient> logger)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromMinutes(15);
        _configuration = configuration;
        _logger = logger;
    }

    public string Model => _configuration["Studio:VideoModel"] ?? "gemini-omni-1.2-flash";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(GeminiKeys.Paid(_configuration));

    public decimal PricePerSecond(string resolution)
    {
        var configured = _configuration[$"Studio:PricePerSecondUsd:{resolution}"];
        if (decimal.TryParse(configured, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p > 0) return p;
        return DefaultPrices.TryGetValue(resolution, out var d) ? d : DefaultPrices["720p"];
    }

    public decimal EstimateCost(int seconds, string resolution) => Math.Round(ClampSeconds(seconds) * PricePerSecond(resolution), 4);

    public static int ClampSeconds(int seconds) => Math.Clamp(seconds, MinSeconds, MaxSeconds);

    /// <summary>Request body for one clip (images first, then the text prompt).</summary>
    public static object BuildRequest(string model, string prompt, IReadOnlyList<OmniReference> references,
        string aspectRatio, string resolution, int seconds)
    {
        var parts = new List<object>();
        foreach (var r in references.Take(MaxReferenceImages))
            parts.Add(new Dictionary<string, object> { ["type"] = "image", ["data"] = Convert.ToBase64String(r.Data), ["mime_type"] = r.MimeType });
        parts.Add(new Dictionary<string, object> { ["type"] = "text", ["text"] = prompt });
        return new Dictionary<string, object>
        {
            ["model"] = model,
            ["input"] = parts,
            ["response_format"] = new Dictionary<string, object>
            {
                ["type"] = "video",
                ["aspect_ratio"] = aspectRatio == "16:9" ? "16:9" : "9:16",
                ["resolution"] = resolution,
                ["duration"] = $"{ClampSeconds(seconds)}s"
            }
        };
    }

    public async Task<OmniClip> GenerateAsync(string prompt, IReadOnlyList<OmniReference> references,
        string aspectRatio, string resolution, int seconds, CancellationToken ct)
    {
        var key = GeminiKeys.Paid(_configuration)
                  ?? throw new InvalidOperationException("Chưa có khoá Gemini trả phí (GEMINI_API_KEY). Omni không có gói miễn phí.");
        seconds = ClampSeconds(seconds);

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(BuildRequest(Model, prompt, references, aspectRatio, resolution, seconds))
        };
        req.Headers.Add("x-goog-api-key", key);

        var started = DateTime.UtcNow;
        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            var reason = ErrorText(body);
            _logger.LogWarning("Omni HTTP {Status}: {Reason}", (int)res.StatusCode, reason);
            if ((int)res.StatusCode is 400 or 403)
                throw new OmniRejectedException($"Omni từ chối yêu cầu (HTTP {(int)res.StatusCode}): {reason}");
            throw new InvalidOperationException($"Omni lỗi HTTP {(int)res.StatusCode}: {reason}");
        }

        var parsed = ParseVideo(body);
        byte[] bytes;
        if (parsed.Data != null) bytes = Convert.FromBase64String(parsed.Data);
        else if (parsed.Uri != null) bytes = await DownloadAsync(parsed.Uri, key, ct);
        else
        {
            var reason = ParseRefusal(body);
            throw new OmniRejectedException(reason ?? "Omni không trả về video (có thể bị bộ lọc an toàn chặn).");
        }

        _logger.LogInformation("Omni clip {Seconds}s {Res} in {Elapsed:F0}s ({Size} KB)", seconds, resolution,
            (DateTime.UtcNow - started).TotalSeconds, bytes.Length / 1024);
        return new OmniClip(bytes, parsed.MimeType ?? "video/mp4", parsed.InteractionId, seconds, EstimateCost(seconds, resolution));
    }

    private async Task<byte[]> DownloadAsync(string uri, string key, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.Add("x-goog-api-key", key);
        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Không tải được video Omni (HTTP {(int)res.StatusCode}).");
        return await res.Content.ReadAsByteArrayAsync(ct);
    }

    public sealed record ParsedVideo(string? Data, string? Uri, string? MimeType, string? InteractionId);

    /// <summary>
    /// Finds the video in an interactions response, whatever the nesting: the first object whose mime_type is video/*
    /// (or whose type is "video") carrying base64 "data" or a "uri"/"file_uri"; plus the interaction id ("id"/"name").
    /// </summary>
    public static ParsedVideo ParseVideo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string? id = root.ValueKind == JsonValueKind.Object
            ? (Str(root, "id") ?? Str(root, "name") ?? Str(root, "interaction_id"))
            : null;
        string? data = null, uri = null, mime = null;

        void Walk(JsonElement e)
        {
            if (data != null || uri != null) return;
            if (e.ValueKind == JsonValueKind.Object)
            {
                var m = Str(e, "mime_type") ?? Str(e, "mimeType");
                var isVideo = (m?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ?? false)
                              || string.Equals(Str(e, "type"), "video", StringComparison.OrdinalIgnoreCase);
                if (isVideo)
                {
                    var d = Str(e, "data");
                    var u = Str(e, "uri") ?? Str(e, "file_uri") ?? Str(e, "fileUri");
                    if (!string.IsNullOrEmpty(d) || !string.IsNullOrEmpty(u))
                    {
                        data = string.IsNullOrEmpty(d) ? null : d;
                        uri = string.IsNullOrEmpty(u) ? null : u;
                        mime = m;
                        return;
                    }
                }
                foreach (var p in e.EnumerateObject()) Walk(p.Value);
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in e.EnumerateArray()) Walk(x);
            }
        }

        Walk(root);
        return new ParsedVideo(data, uri, mime, id);
    }

    private static string? ParseRefusal(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            string? found = null;
            void Walk(JsonElement e)
            {
                if (found != null) return;
                if (e.ValueKind == JsonValueKind.Object)
                {
                    foreach (var key in new[] { "block_reason", "blockReason", "finish_reason", "refusal", "error" })
                        if (e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String) { found = $"{key}: {v.GetString()}"; return; }
                    foreach (var p in e.EnumerateObject()) Walk(p.Value);
                }
                else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Walk(x);
            }
            Walk(doc.RootElement);
            return found == null ? null : $"Omni không tạo video ({found}).";
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ErrorText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.ValueKind == JsonValueKind.Object ? (Str(err, "message") ?? err.GetRawText()) : err.ToString();
        }
        catch (JsonException) { }
        return body.Length > 500 ? body[..500] : body;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
