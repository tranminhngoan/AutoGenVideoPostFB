using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NewsStudio.Infrastructure.Studio;

public sealed record VideoClip(byte[] Video, string MimeType, string? RequestId, int Seconds, decimal CostUsd);

/// <summary>A picture the clip starts from (image-to-video), sent inline to the gateway.</summary>
public sealed record VideoStartImage(byte[] Data, string MediaType);

/// <summary>The provider refused the clip (moderation, invalid argument…): retrying the same prompt will not help.</summary>
public sealed class VideoRejectedException : InvalidOperationException
{
    /// <summary>What the provider still bills for it (0 unless known otherwise).</summary>
    public decimal BilledUsd { get; }

    public VideoRejectedException(string message, decimal billedUsd = 0) : base(message) => BilledUsd = billedUsd;
}

/// <summary>
/// The account can't pay (credits used up, refused / missing key): every further clip would be refused too, so the run
/// stops at once. Nothing was billed. After topping up, "Tạo các clip còn thiếu" carries on.
/// </summary>
public sealed class VideoAccountException : InvalidOperationException
{
    public VideoAccountException(string message) : base(message) { }
}

/// <summary>The provider was busy or failed before producing anything billable (429 / 5xx): safe to retry.</summary>
public sealed class VideoTransientException : InvalidOperationException
{
    public VideoTransientException(string message) : base(message) { }
}

/// <summary>One selectable video model with its per-second prices (USD) by clip resolution.</summary>
public sealed record VideoModelOption(string Id, string Label, IReadOnlyDictionary<string, decimal> PricePerSecond, string Note);

/// <summary>
/// ByteDance Seedance 1.0 (text-to-video, no audio) through the Vercel AI Gateway, with the same HTTP calls the AI SDK's
/// gateway provider makes: POST {base}/video-model/start → an opaque "operation", then POST {base}/video-model/status
/// with that operation until it is completed (videos: url or base64) or errors. The operation is stored on the shot, so
/// a run cut off by a restart picks the same paid job up again. Key: Ai:Gateway:ApiKey / AI_GATEWAY_API_KEY.
/// </summary>
public class SeedanceVideoClient
{
    public const int MinSeconds = 2;
    public const int MaxSeconds = 12;
    public const string FastModel = "bytedance/seedance-v1.0-pro-fast";
    public const string ProModel = "bytedance/seedance-v1.0-pro";
    public static readonly string[] Resolutions = { "480p", "720p", "1080p" };
    private const string DefaultBaseUrl = "https://ai-gateway.vercel.sh/v4/ai";

    // Vercel lists Pro Fast from $0.0097/s and Pro from $0.02/s (480p); higher resolutions scale with the pixel count.
    // Estimates only — override with Studio:Prices:{model}:{resolution}.
    private static readonly Dictionary<string, Dictionary<string, decimal>> DefaultPrices = new()
    {
        [FastModel] = new() { ["480p"] = 0.0097m, ["720p"] = 0.0216m, ["1080p"] = 0.0486m },
        [ProModel] = new() { ["480p"] = 0.02m, ["720p"] = 0.0445m, ["1080p"] = 0.10m },
    };

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SeedanceVideoClient> _logger;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxWait { get; set; } = TimeSpan.FromMinutes(15);

    public SeedanceVideoClient(HttpClient http, IConfiguration configuration, ILogger<SeedanceVideoClient> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public static string? ApiKey(IConfiguration configuration) =>
        NonEmpty(configuration["Ai:Gateway:ApiKey"]) ?? NonEmpty(configuration["AI_GATEWAY_API_KEY"]) ?? NonEmpty(Environment.GetEnvironmentVariable("AI_GATEWAY_API_KEY"));

    public bool IsConfigured => ApiKey(_configuration) != null;

    private string BaseUrl => NonEmpty(_configuration["Ai:Gateway:BaseUrl"])?.TrimEnd('/') ?? DefaultBaseUrl;

    public string DefaultModel => NormalizeModel(_configuration["Studio:VideoModel"]);

    public IReadOnlyList<VideoModelOption> Models() => new[]
    {
        new VideoModelOption(FastModel, "Seedance 1.0 Pro Fast", Prices(FastModel), "Nhanh, rẻ (~3 lần rẻ hơn Pro)"),
        new VideoModelOption(ProModel, "Seedance 1.0 Pro", Prices(ProModel), "Chất lượng cao nhất, chậm và đắt hơn"),
    };

    public static string NormalizeModel(string? model) => model == ProModel ? ProModel : FastModel;

    public static string NormalizeResolution(string? resolution) =>
        Resolutions.FirstOrDefault(r => string.Equals(r, resolution, StringComparison.OrdinalIgnoreCase)) ?? "720p";

    public static int ClampSeconds(int seconds) => Math.Clamp(seconds, MinSeconds, MaxSeconds);

    private Dictionary<string, decimal> Prices(string model) =>
        Resolutions.ToDictionary(r => r, r => PricePerSecond(model, r));

    public decimal PricePerSecond(string? model, string? resolution)
    {
        model = NormalizeModel(model);
        resolution = NormalizeResolution(resolution);
        var configured = _configuration[$"Studio:Prices:{model}:{resolution}"];
        if (decimal.TryParse(configured, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p > 0) return p;
        return DefaultPrices[model][resolution];
    }

    public decimal EstimateCost(string? model, string? resolution, int seconds) =>
        Math.Round(ClampSeconds(seconds) * PricePerSecond(model, resolution), 4);

    /// <summary>Frame size Seedance renders for a resolution and shape ("720x1280"…), as the AI SDK spec writes it.</summary>
    public static string FrameSize(string aspectRatio, string resolution)
    {
        var (w, h) = NormalizeResolution(resolution) switch
        {
            "480p" => (864, 480),
            "1080p" => (1920, 1080),
            _ => (1280, 720)
        };
        return aspectRatio == "16:9" ? $"{w}x{h}" : $"{h}x{w}";
    }

    /// <summary>
    /// Body of POST /video-model/start. <paramref name="resolution"/> null = let the model pick its default.
    /// <paramref name="image"/> (optional) = the first frame, as the AI SDK's gateway provider sends it ({type: file, mediaType, data: base64}).
    /// </summary>
    public static JsonObject BuildStartBody(string prompt, string aspectRatio, string? resolution, int seconds, int? seed, VideoStartImage? image = null)
    {
        var body = new JsonObject
        {
            ["prompt"] = prompt,
            ["n"] = 1,
            ["aspectRatio"] = aspectRatio == "16:9" ? "16:9" : "9:16",
            ["duration"] = ClampSeconds(seconds),
            ["providerOptions"] = new JsonObject
            {
                ["bytedance"] = new JsonObject { ["watermark"] = false, ["cameraFixed"] = false }
            }
        };
        if (resolution != null) body["resolution"] = resolution;
        if (seed != null) body["seed"] = seed.Value;
        if (image != null)
            body["image"] = new JsonObject { ["type"] = "file", ["mediaType"] = image.MediaType, ["data"] = Convert.ToBase64String(image.Data) };
        return body;
    }

    public sealed record JobStatus(string Status, string? VideoUrl, string? VideoBase64, string? MediaType, string? Error);

    /// <summary>Reads a /video-model/status answer: pending | completed (videos[0] url or base64) | error | cancelled.</summary>
    public static JobStatus ParseStatus(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var status = Str(root, "status")?.ToLowerInvariant() ?? "pending";
        string? url = null, data = null, mime = null;
        if (root.TryGetProperty("videos", out var videos) && videos.ValueKind == JsonValueKind.Array && videos.GetArrayLength() > 0)
        {
            var v = videos[0];
            url = Str(v, "url");
            data = Str(v, "data");
            mime = Str(v, "mediaType");
        }
        var error = root.TryGetProperty("error", out var e)
            ? e.ValueKind == JsonValueKind.String ? e.GetString() : e.ValueKind == JsonValueKind.Object ? Str(e, "message") ?? e.GetRawText() : e.GetRawText()
            : null;
        return new JobStatus(status, url, data, mime, error);
    }

    /// <summary>
    /// One clip. <paramref name="onStarted"/> receives the provider's operation as soon as the job is accepted (billed),
    /// to be stored; <paramref name="resumeOperation"/> picks such a stored job up again instead of paying for a new one.
    /// </summary>
    public async Task<VideoClip> GenerateAsync(string model, string prompt, string aspectRatio, string resolution, int seconds, int? seed,
        CancellationToken ct, string? resumeOperation = null, Func<string, Task>? onStarted = null, VideoStartImage? image = null)
    {
        var key = ApiKey(_configuration) ?? throw new VideoAccountException("Chưa có khoá Vercel AI Gateway (biến AI_GATEWAY_API_KEY trên Railway).");
        model = NormalizeModel(model);
        resolution = NormalizeResolution(resolution);
        seconds = ClampSeconds(seconds);
        var cost = EstimateCost(model, resolution, seconds);

        if (resumeOperation != null && await TryFinishAsync(model, resumeOperation, key, ct) is { } resumed)
        {
            _logger.LogInformation("Seedance job resumed without a new charge");
            return new VideoClip(resumed.Bytes, resumed.Mime, resumeOperation, seconds, 0m);
        }

        var operation = await StartAsync(model, key, prompt, aspectRatio, resolution, seconds, seed, ct, image);
        if (onStarted != null) await onStarted(operation);
        var started = DateTime.UtcNow;
        var status = await WaitAsync(model, operation, key, ct);
        if (status.Status != "completed")
        {
            var reason = status.Error ?? status.Status;
            if (LooksLikeModeration(reason))
                throw new VideoRejectedException($"Seedance không tạo clip vì nội dung bị chặn ({reason}) — sửa hành động / lời thoại của shot rồi tạo lại.");
            throw new InvalidOperationException($"Seedance tạo clip lỗi ({reason}).");
        }
        var file = await TakeVideoAsync(status, ct)
                   ?? throw new InvalidOperationException("Không tải được clip từ Vercel — bấm \"Lấy lại clip đã trả tiền\" để thử tải lại (không trả tiền lần nữa).");
        _logger.LogInformation("Seedance clip {Model} {Seconds}s {Res} in {Elapsed:F0}s ({Size} KB)", model, seconds, resolution,
            (DateTime.UtcNow - started).TotalSeconds, file.Bytes.Length / 1024);
        return new VideoClip(file.Bytes, file.Mime, operation, seconds, cost);
    }

    /// <summary>Downloads the clip of an already-paid job again. Never submits anything new — costs nothing.</summary>
    public async Task<(byte[] Bytes, string Mime)?> TryRecoverAsync(string model, string operation, CancellationToken ct)
    {
        var key = ApiKey(_configuration) ?? throw new VideoAccountException("Chưa có khoá Vercel AI Gateway (biến AI_GATEWAY_API_KEY trên Railway).");
        return await TryFinishAsync(NormalizeModel(model), operation, key, ct);
    }

    private async Task<(byte[] Bytes, string Mime)?> TryFinishAsync(string model, string operation, string key, CancellationToken ct)
    {
        try
        {
            var status = await WaitAsync(model, operation, key, ct);
            return status.Status == "completed" ? await TakeVideoAsync(status, ct) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not VideoAccountException)
        {
            _logger.LogInformation("Seedance job not reusable: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Starts the job. The exact resolution format the gateway wants for Seedance 1.0 isn't documented, so a request it
    /// rejects for its resolution (rejections are not billed) is sent again as "720p", then without one.
    /// </summary>
    private async Task<string> StartAsync(string model, string key, string prompt, string aspectRatio, string resolution, int seconds, int? seed, CancellationToken ct,
        VideoStartImage? image = null)
    {
        var candidates = new List<string?> { FrameSize(aspectRatio, resolution), resolution, null };
        VideoRejectedException? last = null;
        foreach (var res in candidates)
        {
            try
            {
                var text = await PostAsync(model, "video-model/start", key, BuildStartBody(prompt, aspectRatio, res, seconds, seed, image), ct);
                using var doc = JsonDocument.Parse(text);
                if (!doc.RootElement.TryGetProperty("operation", out var op) || op.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    throw new InvalidOperationException("Vercel không trả về mã job video.");
                return op.GetRawText();
            }
            catch (VideoRejectedException ex) when (ex.Message.Contains("resolution", StringComparison.OrdinalIgnoreCase) && res != null)
            {
                _logger.LogInformation("Seedance refused resolution {Res}: {Message}", res, ex.Message);
                last = ex;
            }
        }
        throw last ?? new VideoRejectedException("Seedance từ chối độ phân giải.");
    }

    private async Task<JobStatus> WaitAsync(string model, string operation, string key, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var body = new JsonObject { ["operation"] = JsonNode.Parse(operation) };
        while (true)
        {
            try
            {
                var status = ParseStatus(await PostAsync(model, "video-model/status", key, body.DeepClone().AsObject(), ct));
                if (status.Status is "completed" or "error" or "cancelled" or "failed") return status;
            }
            catch (VideoTransientException ex)
            {
                // a hiccup while polling must not lose a paid job: keep waiting until the deadline
                _logger.LogWarning("Seedance poll: {Message}", ex.Message);
            }
            if (DateTime.UtcNow - started > MaxWait)
                throw new InvalidOperationException($"Quá {MaxWait.TotalMinutes:0} phút Seedance chưa trả clip — bấm \"Lấy lại clip đã trả tiền\" sau ít phút (không trả tiền lần nữa).");
            await Task.Delay(PollInterval, ct);
        }
    }

    private async Task<string> PostAsync(string model, string path, string key, JsonObject body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{path}")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Headers.Add("ai-gateway-protocol-version", "0.0.1");
        req.Headers.Add("ai-gateway-auth-method", "api-key");
        req.Headers.Add("ai-video-model-specification-version", "4");
        req.Headers.Add("ai-model-id", model);
        HttpResponseMessage res;
        try { res = await _http.SendAsync(req, ct); }
        catch (HttpRequestException ex) { throw new VideoTransientException($"Không kết nối được Vercel AI Gateway: {ex.Message}"); }
        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode) return text;
            var (message, type) = ErrorOf(text);
            var code = (int)res.StatusCode;
            _logger.LogWarning("Vercel video {Path} HTTP {Status} {Type}: {Message}", path, code, type, message);
            if (code is 401 or 402 or 403 || type is "authentication_error" or "forbidden" or "access_policy_denied" || IsAccountProblem(message))
                throw new VideoAccountException(
                    $"Tài khoản Vercel AI Gateway không dùng được (HTTP {code}: {message}). Kiểm tra khoá AI_GATEWAY_API_KEY và credit " +
                    "(tạo video cần tối thiểu $10 credit). Clip đã tạo vẫn được giữ — nạp xong bấm \"Tạo các clip còn thiếu\".");
            if (code is 429 or >= 500 || type is "rate_limit_exceeded" or "internal_server_error" or "failed_dependency")
                throw new VideoTransientException($"Vercel / Seedance đang bận (HTTP {code}): {message}");
            throw new VideoRejectedException($"Vercel từ chối yêu cầu (HTTP {code}, {type}): {message}");
        }
    }

    private async Task<(byte[] Bytes, string Mime)?> TakeVideoAsync(JobStatus status, CancellationToken ct)
    {
        var mime = status.MediaType is { } m && m.StartsWith("video/") ? m : "video/mp4";
        if (!string.IsNullOrEmpty(status.VideoBase64))
        {
            var bytes = Convert.FromBase64String(status.VideoBase64);
            return bytes.Length < 1024 ? null : (bytes, mime);
        }
        if (string.IsNullOrEmpty(status.VideoUrl)) return null;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                using var dl = await _http.GetAsync(status.VideoUrl, ct);
                if (dl.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Gone) return null;
                dl.EnsureSuccessStatusCode();
                var bytes = await dl.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 1024) return null;
                return (bytes, dl.Content.Headers.ContentType?.MediaType is { } dm && dm.StartsWith("video/") ? dm : mime);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Seedance clip download attempt {Attempt} failed", attempt);
                if (attempt < 4) await Task.Delay(PollInterval * attempt, ct);
            }
        }
        return null;
    }

    public static bool IsAccountProblem(string message) =>
        new[] { "credit", "balance", "billing", "insufficient", "payment", "top up", "top-up" }
            .Any(w => message.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeModeration(string reason) =>
        new[] { "sensitive", "policy", "moderation", "safety", "inappropriate", "real person", "copyright" }
            .Any(w => reason.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static (string Message, string? Type) ErrorOf(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.ValueKind == JsonValueKind.Object ? (Str(err, "message") ?? err.GetRawText(), Str(err, "type")) : (err.ToString(), null);
            if (Str(doc.RootElement, "message") is { } m) return (m, null);
        }
        catch (JsonException) { }
        return (body.Length > 400 ? body[..400] : body, null);
    }

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
