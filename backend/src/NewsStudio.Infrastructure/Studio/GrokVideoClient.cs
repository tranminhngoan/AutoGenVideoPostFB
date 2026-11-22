using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NewsStudio.Infrastructure.Studio;

/// <summary>The picture a clip starts from (image-to-video). JPEG already cropped to the clip's frame.</summary>
public sealed record StartFrame(byte[] Data, string MimeType);

/// <summary>xAI was busy or failed before producing anything billable (HTTP 429/5xx, internal error): safe to retry.</summary>
public sealed class VideoTransientException : InvalidOperationException
{
    public VideoTransientException(string message) : base(message) { }
}

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

    /// <summary>
    /// One clip. <paramref name="onStarted"/> gets the request id as soon as xAI accepts (and bills) the job, so the
    /// caller can store it; <paramref name="resumeRequestId"/> picks up such a stored job after a restart instead of
    /// paying for a new one (a job xAI no longer knows is started afresh).
    /// </summary>
    public async Task<VideoClip> GenerateAsync(string prompt, StartFrame? frame, string aspectRatio, string resolution, int seconds,
        CancellationToken ct, string? resumeRequestId = null, Func<string, Task>? onStarted = null)
    {
        var key = ApiKey(_configuration) ?? throw new InvalidOperationException("Chưa có khoá xAI (biến XAI_API_KEY trên Railway).");
        seconds = ClampSeconds(seconds);
        resolution = NormalizeResolution(resolution);
        var cost = EstimateCost(seconds, resolution, frame != null);
        var started = DateTime.UtcNow;

        JobStatus? status = null;
        var requestId = resumeRequestId;
        if (requestId != null)
        {
            // only a job still running or finished cleanly is worth picking up; anything else is started afresh
            status = await WaitAsync(requestId, key, ct);
            if (status is not { Status: "done", VideoUrl: not null } || status.RespectModeration == false) status = null;
            else if (await TryDownloadAsync(status.VideoUrl, ct) is { } resumed)
            {
                _logger.LogInformation("xAI job {Id} resumed without a new charge", requestId);
                return new VideoClip(resumed.Bytes, resumed.Mime, requestId, seconds, 0m);
            }
            else status = null;
            if (status == null) _logger.LogInformation("xAI job {Id} not reusable — starting a new one", requestId);
        }
        if (status == null)
        {
            requestId = await StartAsync(key, BuildRequest(Model, prompt, frame, aspectRatio, resolution, seconds), ct);
            if (onStarted != null) await onStarted(requestId);
            status = await WaitAsync(requestId, key, ct)
                     ?? throw new InvalidOperationException($"xAI không còn thấy clip {requestId} vừa tạo.");
        }

        if (status.Status == "failed")
        {
            var reason = $"{status.ErrorCode}: {status.ErrorMessage}".Trim(' ', ':');
            if (status.ErrorCode is "service_unavailable" or "internal_error")
                throw new VideoTransientException($"xAI tạm lỗi khi tạo clip ({reason}).");
            throw new VideoRejectedException($"xAI không tạo clip ({reason}).");
        }
        if (status.VideoUrl == null)
            throw new InvalidOperationException($"Clip {requestId} hết hạn trước khi tải được.");
        if (status.RespectModeration == false)
            throw new VideoRejectedException("xAI đã lọc clip này vì vi phạm chính sách nội dung — sửa hành động / lời thoại / ảnh của shot rồi tạo lại.", cost);

        // download promptly (the URL is temporary); a flaky download is retried, never the paid job
        var file = await TryDownloadAsync(status.VideoUrl, ct)
                   ?? throw new InvalidOperationException($"Không tải được clip {requestId} từ xAI — bấm tạo lại shot để thử tải lại (không trả tiền lần nữa nếu clip còn trên xAI).");
        _logger.LogInformation("xAI clip {Seconds}s {Res} in {Elapsed:F0}s ({Size} KB)", seconds, resolution,
            (DateTime.UtcNow - started).TotalSeconds, file.Bytes.Length / 1024);
        return new VideoClip(file.Bytes, file.Mime, requestId, seconds, cost);
    }

    /// <summary>Up to 4 tries; null when the file can't be fetched (or is clearly not a video).</summary>
    private async Task<(byte[] Bytes, string Mime)?> TryDownloadAsync(string url, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                using var dl = await _http.GetAsync(url, ct);
                if (dl.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Gone) return null;
                dl.EnsureSuccessStatusCode();
                var bytes = await dl.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 1024) return null;
                var mime = dl.Content.Headers.ContentType?.MediaType is { } m && m.StartsWith("video/") ? m : "video/mp4";
                return (bytes, mime);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "xAI clip download attempt {Attempt} failed", attempt);
                if (attempt < 4) await Task.Delay(PollInterval * attempt, ct);
            }
        }
        return null;
    }

    private async Task<string> StartAsync(string key, Dictionary<string, object> body, CancellationToken ct)
    {
        using var start = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/generations") { Content = JsonContent.Create(body) };
        start.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var res = await _http.SendAsync(start, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            var reason = ErrorText(text);
            var code = (int)res.StatusCode;
            _logger.LogWarning("xAI video HTTP {Status}: {Reason}", code, reason);
            if (code is 429 or >= 500) throw new VideoTransientException($"xAI đang bận / lỗi tạm thời (HTTP {code}): {reason}");
            if (code is 401 or 402 or 403)
                throw new VideoRejectedException($"xAI từ chối (HTTP {code}): {reason} — kiểm tra khoá XAI_API_KEY và số dư tài khoản xAI.");
            throw new VideoRejectedException($"xAI từ chối yêu cầu (HTTP {code}): {reason}");
        }
        using var doc = JsonDocument.Parse(text);
        return Str(doc.RootElement, "request_id") ?? Str(doc.RootElement, "id") ?? throw new InvalidOperationException("xAI không trả về request_id.");
    }

    /// <summary>Polls until done / failed / expired. Null when xAI doesn't know the id (404).</summary>
    private async Task<JobStatus?> WaitAsync(string requestId, string key, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            await Task.Delay(PollInterval, ct);
            using var poll = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{requestId}");
            poll.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var res = await _http.SendAsync(poll, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            if (res.IsSuccessStatusCode)
            {
                var status = ParseStatus(body);
                if (status.Status is "done" or "failed" or "expired") return status;
            }
            else
            {
                // a transient error while polling must not lose a paid job: keep waiting until the deadline
                _logger.LogWarning("xAI poll {Id} HTTP {Status}: {Body}", requestId, (int)res.StatusCode, ErrorText(body));
            }
            if (DateTime.UtcNow - started > MaxWait)
                throw new InvalidOperationException($"Quá {MaxWait.TotalMinutes:0} phút xAI chưa trả clip {requestId} — bấm tạo lại shot sau ít phút (sẽ lấy tiếp clip này, không trả tiền lần nữa).");
        }
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
