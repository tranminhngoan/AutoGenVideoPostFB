using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NewsStudio.Infrastructure.Studio;

/// <summary>A made clip. <paramref name="HadAudio"/>: the provider returned a sound track (it was removed, and billed at the audio price).</summary>
public sealed record VideoClip(byte[] Video, string MimeType, string? RequestId, int Seconds, decimal CostUsd, bool HadAudio = false);

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
/// The video model of every studio page: Google Veo 3.1 Lite (text-to-video / image-to-video) through the Vercel AI
/// Gateway. (The class keeps its old name: it made ByteDance Seedance 1.0 clips until 2026-11-27 — those model ids are
/// kept only so a job paid on Seedance can still be fetched again for free.) Same HTTP calls as the AI SDK's gateway
/// provider: POST {base}/video-model/start → an opaque "operation", then POST {base}/video-model/status with it until
/// the job is completed (videos: url or base64) or errors. The operation is stored on the shot, so a run cut off by a
/// restart picks the same paid job up again. Key: Ai:Gateway:ApiKey / AI_GATEWAY_API_KEY.
/// Veo makes 4, 6 or 8 s clips at 720p / 1080p in 9:16 or 16:9, and makes sound unless told not to: the studios add
/// their own narration / music, so sound is switched off (cheaper) and a sound track that still comes back is removed.
/// The prompt's trailing "Avoid: …" goes into Veo's own negative prompt.
/// </summary>
public class SeedanceVideoClient
{
    public const string VeoLiteModel = "google/veo-3.1-lite-generate-001";
    /// <summary>Old Seedance ids: only to fetch again a clip already paid on Seedance (never for a new clip).</summary>
    public const string FastModel = "bytedance/seedance-v1.0-pro-fast";
    public const string ProModel = "bytedance/seedance-v1.0-pro";

    /// <summary>The clip lengths Veo makes (seconds).</summary>
    public static readonly int[] AllowedSeconds = { 4, 6, 8 };
    public const int MinSeconds = 4;
    public const int MaxSeconds = 8;
    public static readonly string[] Resolutions = { "720p", "1080p" };
    private const string DefaultBaseUrl = "https://ai-gateway.vercel.sh/v4/ai";

    // Veo 3.1 Lite: $0.03/s at 720p and $0.05/s at 1080p without sound; with sound $0.05 / $0.08. Override with
    // Studio:Prices:{model}:{resolution} (and Studio:AudioPrices:… for the sound price) if Vercel's rate changes.
    private static readonly Dictionary<string, decimal> SilentPrices = new() { ["720p"] = 0.03m, ["1080p"] = 0.05m };
    private static readonly Dictionary<string, decimal> AudioPrices = new() { ["720p"] = 0.05m, ["1080p"] = 0.08m };

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SeedanceVideoClient> _logger;
    private readonly StudioMedia? _media;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxWait { get; set; } = TimeSpan.FromMinutes(15);

    public SeedanceVideoClient(HttpClient http, IConfiguration configuration, ILogger<SeedanceVideoClient> logger, StudioMedia? media = null)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
        _media = media;
    }

    public static string? ApiKey(IConfiguration configuration) =>
        NonEmpty(configuration["Ai:Gateway:ApiKey"]) ?? NonEmpty(configuration["AI_GATEWAY_API_KEY"]) ?? NonEmpty(Environment.GetEnvironmentVariable("AI_GATEWAY_API_KEY"));

    public bool IsConfigured => ApiKey(_configuration) != null;

    private string BaseUrl => NonEmpty(_configuration["Ai:Gateway:BaseUrl"])?.TrimEnd('/') ?? DefaultBaseUrl;

    public string DefaultModel => VeoLiteModel;

    public IReadOnlyList<VideoModelOption> Models() => new[]
    {
        new VideoModelOption(VeoLiteModel, "Google Veo 3.1 Lite", Prices(), "Clip 4, 6 hoặc 8 giây · không tiếng (lời bình / nhạc thêm sau)"),
    };

    /// <summary>Every new clip is made by Veo 3.1 Lite, whatever an older video or project has stored (e.g. a Seedance id).</summary>
    public static string NormalizeModel(string? model) => VeoLiteModel;

    public static string NormalizeResolution(string? resolution) =>
        Resolutions.FirstOrDefault(r => string.Equals(r, resolution?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "720p";

    /// <summary>
    /// The clip length Veo can make for a wanted length: 4, 6 or 8 s, rounded UP between them (5 → 6, 7 → 8) so a clip
    /// is never shorter than asked within range; at most 8 (a longer scene is split before it gets here).
    /// </summary>
    public static int ClampSeconds(int seconds) => seconds <= MinSeconds ? MinSeconds : seconds >= MaxSeconds ? MaxSeconds : (seconds + 1) / 2 * 2;

    /// <summary>The nearest length Veo makes (for a measured length: 4.9 → 4, 5.0 → 6).</summary>
    public static int NearestSeconds(double seconds) => AllowedSeconds.OrderBy(a => Math.Abs(a - seconds)).ThenByDescending(a => a).First();

    private Dictionary<string, decimal> Prices() => Resolutions.ToDictionary(r => r, r => PricePerSecond(VeoLiteModel, r));

    public decimal PricePerSecond(string? model, string? resolution) => Price("Studio:Prices", SilentPrices, resolution);

    private decimal AudioPricePerSecond(string? resolution) => Price("Studio:AudioPrices", AudioPrices, resolution);

    private decimal Price(string section, Dictionary<string, decimal> defaults, string? resolution)
    {
        resolution = NormalizeResolution(resolution);
        var configured = _configuration[$"{section}:{VeoLiteModel}:{resolution}"];
        if (decimal.TryParse(configured, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p > 0) return p;
        return defaults[resolution];
    }

    public decimal EstimateCost(string? model, string? resolution, int seconds) =>
        Math.Round(ClampSeconds(seconds) * PricePerSecond(model, resolution), 4);

    /// <summary>Frame size for a resolution and shape ("720x1280"…), as the AI SDK spec writes it (WIDTHxHEIGHT).</summary>
    public static string FrameSize(string aspectRatio, string resolution)
    {
        var (w, h) = NormalizeResolution(resolution) == "1080p" ? (1920, 1080) : (1280, 720);
        return aspectRatio == "16:9" ? $"{w}x{h}" : $"{h}x{w}";
    }

    /// <summary>The engine's prompt split into what to show and its trailing "Avoid: …" (Veo's negative prompt).</summary>
    public static (string Prompt, string? Negative) SplitNegative(string prompt)
    {
        var i = prompt.LastIndexOf(" Avoid: ", StringComparison.Ordinal);
        if (i < 0) return (prompt.Trim(), null);
        var negative = prompt[(i + " Avoid: ".Length)..].Trim().TrimEnd('.').Trim();
        return negative.Length == 0 ? (prompt[..i].Trim(), null) : (prompt[..i].Trim(), negative);
    }

    /// <summary>What can still be changed in a request the provider refused for one of its parameters.</summary>
    public sealed record StartOptions(string? Resolution, string? PersonGeneration = "allow_all", bool TopLevelAudio = true, bool SendSeed = true, bool SeparateNegative = true);

    /// <summary>
    /// Body of POST /video-model/start. <paramref name="resolution"/> null = the model's default.
    /// <paramref name="image"/> (optional) = the first frame, as the AI SDK's gateway provider sends it ({type: file, mediaType, data: base64}).
    /// </summary>
    public static JsonObject BuildStartBody(string prompt, string aspectRatio, string? resolution, int seconds, int? seed, VideoStartImage? image = null,
        StartOptions? options = null)
    {
        var o = options ?? new StartOptions(resolution);
        var (text, negative) = o.SeparateNegative ? SplitNegative(prompt) : (prompt.Trim(), null);
        var vertex = new JsonObject { ["generateAudio"] = false };
        if (negative != null) vertex["negativePrompt"] = negative;
        if (o.PersonGeneration != null) vertex["personGeneration"] = o.PersonGeneration;
        var body = new JsonObject
        {
            ["prompt"] = text,
            ["n"] = 1,
            ["aspectRatio"] = aspectRatio == "16:9" ? "16:9" : "9:16",
            ["duration"] = ClampSeconds(seconds),
            ["providerOptions"] = new JsonObject { ["vertex"] = vertex }
        };
        if (o.TopLevelAudio) body["generateAudio"] = false;
        if (resolution != null) body["resolution"] = resolution;
        if (seed != null && o.SendSeed) body["seed"] = seed.Value;
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
        resolution = NormalizeResolution(resolution);
        seconds = ClampSeconds(seconds);

        if (resumeOperation != null && await TryFinishAnyAsync(model, resumeOperation, key, ct) is { } resumed)
        {
            _logger.LogInformation("Video job resumed without a new charge");
            var (bytes, _) = await SilentAsync(resumed.Bytes, ct);
            return new VideoClip(bytes, resumed.Mime, resumeOperation, seconds, 0m);
        }

        var (operation, usedResolution) = await StartAsync(key, prompt, aspectRatio, resolution, seconds, seed, ct, image);
        if (onStarted != null) await onStarted(operation);
        var started = DateTime.UtcNow;
        var status = await WaitAsync(VeoLiteModel, operation, key, ct);
        if (status.Status != "completed")
        {
            var reason = status.Error ?? status.Status;
            if (LooksLikeModeration(reason))
                throw new VideoRejectedException($"Veo không tạo clip vì bộ lọc an toàn của Google chặn ({reason}) — sửa hành động / prompt của cảnh rồi tạo lại.");
            throw new InvalidOperationException($"Veo tạo clip lỗi ({reason}).");
        }
        if (string.IsNullOrEmpty(status.VideoUrl) && string.IsNullOrEmpty(status.VideoBase64))
            // Google answers "done" with no video when its safety filter removed it (not billed)
            throw new VideoRejectedException("Veo không trả clip — thường do bộ lọc an toàn của Google chặn (trẻ em, người thật, hành động nguy hiểm…). " +
                                             "Sửa prompt cảnh này cho nhẹ nhàng hơn rồi tạo lại.");
        var file = await TakeVideoAsync(status, ct)
                   ?? throw new InvalidOperationException("Không tải được clip từ Vercel — bấm \"Lấy lại clip đã trả tiền\" để thử tải lại (không trả tiền lần nữa).");
        var (video, hadAudio) = await SilentAsync(file.Bytes, ct);
        var cost = Math.Round(seconds * (hadAudio ? AudioPricePerSecond(usedResolution) : PricePerSecond(VeoLiteModel, usedResolution)), 4);
        if (hadAudio) _logger.LogWarning("Veo returned a sound track although sound was switched off — removed; cost counted at the sound price");
        _logger.LogInformation("Veo clip {Seconds}s {Res} in {Elapsed:F0}s ({Size} KB)", seconds, usedResolution,
            (DateTime.UtcNow - started).TotalSeconds, video.Length / 1024);
        return new VideoClip(video, file.Mime, operation, seconds, cost, hadAudio);
    }

    /// <summary>Downloads the clip of an already-paid job again. Never submits anything new — costs nothing.</summary>
    public async Task<(byte[] Bytes, string Mime)?> TryRecoverAsync(string model, string operation, CancellationToken ct)
    {
        var key = ApiKey(_configuration) ?? throw new VideoAccountException("Chưa có khoá Vercel AI Gateway (biến AI_GATEWAY_API_KEY trên Railway).");
        if (await TryFinishAnyAsync(model, operation, key, ct) is not { } file) return null;
        return ((await SilentAsync(file.Bytes, ct)).Video, file.Mime);
    }

    /// <summary>
    /// A stored job may have been paid on Seedance (before the switch) or on Veo: its status is asked from the stored
    /// model first, then the others — status calls are free.
    /// </summary>
    private async Task<(byte[] Bytes, string Mime)?> TryFinishAnyAsync(string? model, string operation, string key, CancellationToken ct)
    {
        var models = new[] { model?.Trim(), VeoLiteModel, FastModel, ProModel }.Where(m => !string.IsNullOrEmpty(m)).Distinct().Cast<string>();
        foreach (var m in models)
            if (await TryFinishAsync(m, operation, key, ct) is { } file) return file;
        return null;
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
            _logger.LogInformation("Video job not reusable with {Model}: {Message}", model, ex.Message);
            return null;
        }
    }

    /// <summary>The clip without a sound track (the studios add their own sound); true when it had one.</summary>
    private async Task<(byte[] Video, bool HadAudio)> SilentAsync(byte[] video, CancellationToken ct)
    {
        if (!HasAudioTrack(video)) return (video, false);
        if (_media != null && await _media.StripAudioAsync(video, ct) is { } silent) return (silent, true);
        _logger.LogWarning("Video clip has a sound track that could not be removed");
        return (video, true);
    }

    /// <summary>An MP4 with an audio track has a handler box of type "soun" (hdlr: size, type, version/flags, pre-defined, handler).</summary>
    public static bool HasAudioTrack(byte[] mp4)
    {
        for (var i = 0; i + 16 <= mp4.Length; i++)
            if (mp4[i] == 'h' && mp4[i + 1] == 'd' && mp4[i + 2] == 'l' && mp4[i + 3] == 'r'
                && mp4[i + 12] == 's' && mp4[i + 13] == 'o' && mp4[i + 14] == 'u' && mp4[i + 15] == 'n')
                return true;
        return false;
    }

    /// <summary>
    /// Starts the job. A request refused for one of its parameters (refusals are not billed) is sent again without it:
    /// the frame size as "720p" / "1080p", then none; children allowed → adults only → the provider's default; the
    /// top-level sound switch, the seed, the separate negative prompt. 1080p refused for the clip length → 720p.
    /// </summary>
    private async Task<(string Operation, string Resolution)> StartAsync(string key, string prompt, string aspectRatio, string resolution, int seconds, int? seed,
        CancellationToken ct, VideoStartImage? image = null)
    {
        var sizes = new List<(string? Value, string Res)> { (FrameSize(aspectRatio, resolution), resolution), (resolution, resolution) };
        if (resolution != "720p") sizes.Add((FrameSize(aspectRatio, "720p"), "720p"));
        sizes.Add((null, "720p"));
        var size = 0;
        var options = new StartOptions(sizes[0].Value);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var body = BuildStartBody(prompt, aspectRatio, sizes[size].Value, seconds, seed, image, options);
                var text = await PostAsync(VeoLiteModel, "video-model/start", key, body, ct);
                using var doc = JsonDocument.Parse(text);
                if (!doc.RootElement.TryGetProperty("operation", out var op) || op.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    throw new InvalidOperationException("Vercel không trả về mã job video.");
                return (op.GetRawText(), sizes[size].Res);
            }
            catch (VideoRejectedException ex) when (attempt < 10)
            {
                var m = ex.Message.ToLowerInvariant();
                var changed = false;
                // people of all ages (the childhood videos need children) → adults only → the provider's default
                if (m.Contains("person") && options.PersonGeneration != null)
                {
                    options = options with { PersonGeneration = options.PersonGeneration == "allow_all" ? "allow_adult" : null };
                    changed = true;
                }
                else if (LooksLikeModeration(ex.Message)) throw;
                else if (m.Contains("generateaudio") && options.TopLevelAudio)
                {
                    options = options with { TopLevelAudio = false };
                    changed = true;
                }
                else if (m.Contains("seed") && options.SendSeed && seed != null)
                {
                    options = options with { SendSeed = false };
                    changed = true;
                }
                else if (m.Contains("negative") && options.SeparateNegative)
                {
                    options = options with { SeparateNegative = false };
                    changed = true;
                }
                else if ((m.Contains("resolution") || (m.Contains("duration") && sizes[size].Res != "720p")) && size + 1 < sizes.Count)
                {
                    size++;
                    changed = true;
                }
                if (!changed) throw;
                _logger.LogInformation("Veo refused a parameter, sending again: {Message}", ex.Message);
            }
        }
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
                _logger.LogWarning("Video poll: {Message}", ex.Message);
            }
            if (DateTime.UtcNow - started > MaxWait)
                throw new InvalidOperationException($"Quá {MaxWait.TotalMinutes:0} phút Veo chưa trả clip — bấm \"Lấy lại clip đã trả tiền\" sau ít phút (không trả tiền lần nữa).");
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
                throw new VideoTransientException($"Vercel / Veo đang bận (HTTP {code}): {message}");
            if (LooksLikeModeration(message))
                throw new VideoRejectedException($"Veo không nhận prompt vì bộ lọc an toàn của Google ({message}) — sửa hành động / prompt của cảnh rồi tạo lại.");
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
                _logger.LogWarning(ex, "Video clip download attempt {Attempt} failed", attempt);
                if (attempt < 4) await Task.Delay(PollInterval * attempt, ct);
            }
        }
        return null;
    }

    public static bool IsAccountProblem(string message) =>
        new[] { "credit", "balance", "billing", "insufficient", "payment", "top up", "top-up" }
            .Any(w => message.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeModeration(string reason) =>
        new[] { "sensitive", "policy", "moderation", "safety", "inappropriate", "real person", "copyright", "usage guidelines", "violat",
                "responsible ai", "raimediafiltered", "filtered", "prohibited", "blocked" }
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
