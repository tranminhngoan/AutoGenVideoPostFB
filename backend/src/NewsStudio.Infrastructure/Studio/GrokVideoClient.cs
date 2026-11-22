using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NewsStudio.Infrastructure.Studio;

/// <summary>The picture a clip starts from (image-to-video). JPEG already cropped to the clip's frame.</summary>
public sealed record StartFrame(byte[] Data, string MimeType);

public sealed record VideoClip(byte[] Video, string MimeType, string? RequestId, int Seconds, decimal CostUsd);

/// <summary>xAI refused or filtered the clip (moderation, bad argument…): retrying the same prompt will not help.</summary>
public sealed class VideoRejectedException : InvalidOperationException
{
    /// <summary>What xAI still bills for it (a generated-then-filtered clip is charged).</summary>
    public decimal BilledUsd { get; }

    public VideoRejectedException(string message, decimal billedUsd = 0) : base(message) => BilledUsd = billedUsd;
}

/// <summary>
/// xAI Grok Imagine video (model grok-imagine-video-1.5-lite): POST /v1/videos/generations starts a job, then
/// GET /v1/videos/{request_id} is polled until "done" and the temporary video URL is downloaded. One clip is 1–15 s,
/// with its own audio (speech with lip sync, sound effects). Lite takes no reference images — only an optional start
/// frame — so character continuity comes from the start frame (asset image or the previous clip's last frame).
/// Key: Ai:Xai:ApiKey / XAI_API_KEY. Cost = seconds × the price of the resolution (+ a per-input-image fee).
/// </summary>
public class GrokVideoClient
{
    public const int MinSeconds = 1;
    public const int MaxSeconds = 15;
    public const string DefaultModel = "grok-imagine-video-1.5-lite";
    public static readonly string[] Resolutions = { "480p", "720p", "1080p" };
    private const string BaseUrl = "https://api.x.ai/v1/videos";

    // xAI lists lite at 480p $0.02 · 720p $0.03 · 1080p $0.14 per second (its model card shows the 480p rate).
    private static readonly Dictionary<string, decimal> DefaultPrices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["480p"] = 0.02m, ["720p"] = 0.03m, ["1080p"] = 0.14m
    };

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GrokVideoClient> _logger;

    /// <summary>Time between status checks (tests shorten it).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxWait { get; set; } = TimeSpan.FromMinutes(20);

    public GrokVideoClient(HttpClient http, IConfiguration configuration, ILogger<GrokVideoClient> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public string Model => NonEmpty(_configuration["Studio:VideoModel"]) ?? DefaultModel;

    public static string? ApiKey(IConfiguration configuration) =>
        NonEmpty(configuration["Ai:Xai:ApiKey"]) ?? NonEmpty(configuration["XAI_API_KEY"]) ?? NonEmpty(Environment.GetEnvironmentVariable("XAI_API_KEY"));

    public bool IsConfigured => ApiKey(_configuration) != null;

    public decimal PricePerSecond(string resolution)
    {
        var configured = _configuration[$"Studio:PricePerSecondUsd:{resolution}"];
        if (decimal.TryParse(configured, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p > 0) return p;
        return DefaultPrices.TryGetValue(resolution, out var d) ? d : DefaultPrices["720p"];
    }

    public decimal ImageInputFeeUsd =>
        decimal.TryParse(_configuration["Studio:ImageInputFeeUsd"], NumberStyles.Number, CultureInfo.InvariantCulture, out var f) && f >= 0 ? f : 0.01m;

    public decimal EstimateCost(int seconds, string resolution, bool withStartFrame) =>
        Math.Round(ClampSeconds(seconds) * PricePerSecond(resolution) + (withStartFrame ? ImageInputFeeUsd : 0), 4);

    public static int ClampSeconds(int seconds) => Math.Clamp(seconds, MinSeconds, MaxSeconds);

    public static string NormalizeResolution(string? resolution) =>
        Resolutions.FirstOrDefault(r => string.Equals(r, resolution, StringComparison.OrdinalIgnoreCase)) ?? "720p";

    /// <summary>Request body of one clip. The start frame goes inline as a base64 data URI.</summary>
    public static Dictionary<string, object> BuildRequest(string model, string prompt, StartFrame? frame,
        string aspectRatio, string resolution, int seconds)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["duration"] = ClampSeconds(seconds),
            ["aspect_ratio"] = aspectRatio == "16:9" ? "16:9" : "9:16",
            ["resolution"] = NormalizeResolution(resolution),
            ["generate_audio"] = true
        };
        if (frame != null)
            body["image"] = new Dictionary<string, object> { ["url"] = $"data:{frame.MimeType};base64,{Convert.ToBase64String(frame.Data)}" };
        return body;
    }

    public sealed record JobStatus(string Status, string? VideoUrl, bool? RespectModeration, string? ErrorCode, string? ErrorMessage);

    /// <summary>Reads a GET /v1/videos/{id} answer.</summary>
    public static JobStatus ParseStatus(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string? url = null, code = null, message = null;
        bool? respect = null;
        if (root.TryGetProperty("video", out var v) && v.ValueKind == JsonValueKind.Object)
        {
            url = Str(v, "url");
            if (v.TryGetProperty("respect_moderation", out var r) && r.ValueKind is JsonValueKind.True or JsonValueKind.False) respect = r.GetBoolean();
        }
        if (root.TryGetProperty("error", out var e))
        {
            if (e.ValueKind == JsonValueKind.Object) { code = Str(e, "code"); message = Str(e, "message"); }
            else if (e.ValueKind == JsonValueKind.String) message = e.GetString();
        }
        var status = Str(root, "status") ?? (url != null ? "done" : "pending");
        return new JobStatus(status.ToLowerInvariant(), url, respect, code, message);
    }

    public async Task<VideoClip> GenerateAsync(string prompt, StartFrame? frame, string aspectRatio, string resolution, int seconds, CancellationToken ct)
    {
        var key = ApiKey(_configuration) ?? throw new InvalidOperationException("Chưa có khoá xAI (biến XAI_API_KEY trên Railway).");
        seconds = ClampSeconds(seconds);
        resolution = NormalizeResolution(resolution);
        var cost = EstimateCost(seconds, resolution, frame != null);

        // 1. start the job
        using var start = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/generations")
        {
            Content = JsonContent.Create(BuildRequest(Model, prompt, frame, aspectRatio, resolution, seconds))
        };
        start.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        string requestId;
        using (var res = await _http.SendAsync(start, ct))
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                var reason = ErrorText(body);
                _logger.LogWarning("xAI video HTTP {Status}: {Reason}", (int)res.StatusCode, reason);
                if ((int)res.StatusCode is 400 or 403 or 422)
                    throw new VideoRejectedException($"xAI từ chối yêu cầu (HTTP {(int)res.StatusCode}): {reason}");
                throw new InvalidOperationException($"xAI lỗi HTTP {(int)res.StatusCode}: {reason}");
            }
            using var doc = JsonDocument.Parse(body);
            requestId = Str(doc.RootElement, "request_id") ?? Str(doc.RootElement, "id")
                        ?? throw new InvalidOperationException("xAI không trả về request_id.");
        }

        // 2. wait for it
        var started = DateTime.UtcNow;
        JobStatus status;
        while (true)
        {
            await Task.Delay(PollInterval, ct);
            using var poll = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{requestId}");
            poll.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var res = await _http.SendAsync(poll, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                // a transient error while polling must not lose a paid job: keep waiting until the deadline
                _logger.LogWarning("xAI poll {Id} HTTP {Status}: {Body}", requestId, (int)res.StatusCode, ErrorText(body));
                if (DateTime.UtcNow - started > MaxWait) throw new InvalidOperationException($"xAI không phản hồi trạng thái clip {requestId}.");
                continue;
            }
            status = ParseStatus(body);
            if (status.Status is "done" or "failed" or "expired") break;
            if (DateTime.UtcNow - started > MaxWait)
                throw new InvalidOperationException($"Quá {MaxWait.TotalMinutes:0} phút xAI chưa tạo xong clip ({requestId}).");
        }

        if (status.Status == "failed")
        {
            var reason = $"{status.ErrorCode}: {status.ErrorMessage}".Trim(' ', ':');
            if (status.ErrorCode is "invalid_argument" or "permission_denied" or "failed_precondition")
                throw new VideoRejectedException($"xAI không tạo clip ({reason}).");
            throw new InvalidOperationException($"xAI tạo clip lỗi ({reason}).");
        }
        if (status.Status == "expired" || status.VideoUrl == null)
            throw new InvalidOperationException($"Clip {requestId} hết hạn trước khi tải được.");
        if (status.RespectModeration == false)
            throw new VideoRejectedException("xAI đã lọc clip này vì vi phạm chính sách nội dung — sửa hành động / lời thoại của shot rồi tạo lại.", cost);

        // 3. download promptly (the URL is temporary)
        using var dl = await _http.GetAsync(status.VideoUrl, ct);
        if (!dl.IsSuccessStatusCode) throw new InvalidOperationException($"Không tải được clip từ xAI (HTTP {(int)dl.StatusCode}).");
        var bytes = await dl.Content.ReadAsByteArrayAsync(ct);
        _logger.LogInformation("xAI clip {Seconds}s {Res} in {Elapsed:F0}s ({Size} KB)", seconds, resolution,
            (DateTime.UtcNow - started).TotalSeconds, bytes.Length / 1024);
        return new VideoClip(bytes, dl.Content.Headers.ContentType?.MediaType is { } m && m.StartsWith("video/") ? m : "video/mp4",
            requestId, seconds, cost);
    }

    private static string ErrorText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
                return err.ValueKind == JsonValueKind.Object ? (Str(err, "message") ?? err.GetRawText()) : err.ToString();
            if (Str(root, "message") is { } msg) return msg;
        }
        catch (JsonException) { }
        return body.Length > 500 ? body[..500] : body;
    }

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
