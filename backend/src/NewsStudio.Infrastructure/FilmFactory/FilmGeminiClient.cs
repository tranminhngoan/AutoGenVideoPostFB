using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Infrastructure.Ai;
using NewsStudio.Infrastructure.Common;

namespace NewsStudio.Infrastructure.FilmFactory;

/// <summary>
/// Gemini calls used by the Film Factory: JSON text (story bible, characters, episode scripts) and
/// still-frame image generation. Free key first, paid key after (see <see cref="GeminiKeys"/>).
/// Every call reports what it really cost, so the project budget can be enforced. Never returns a
/// made-up answer: failures throw with Google's own explanation.
/// </summary>
public class FilmGeminiClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly AiUsageMeter _meter;
    private readonly ILogger<FilmGeminiClient> _logger;

    public FilmGeminiClient(HttpClient http, IConfiguration configuration, AiUsageMeter meter, ILogger<FilmGeminiClient> logger)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromMinutes(8); // a long script → a long shot plan (runs in the background)
        _configuration = configuration;
        _meter = meter;
        _logger = logger;
    }

    public sealed record TextResult(string Text, decimal CostUsd, bool FreeKey, long Tokens);

    public sealed record ImageResult(byte[] Bytes, string MimeType, decimal CostUsd, bool FreeKey, long Tokens);

    /// <summary>One reference picture sent with an image request (e.g. a character's face).</summary>
    public sealed record ReferenceImage(byte[] Bytes, string MimeType);

    public string TextModel => _configuration["Film:TextModel"] ?? _configuration["Ai:Gemini:Model"] ?? "gemini-flash-latest";

    public string ImageModel => _configuration["Film:ImageModel"] ?? "gemini-3.1-flash-lite-image";

    /// <summary>Price of one generated frame, used for the budget check before the call (USD).</summary>
    public decimal EstimatedImageCostUsd => Math.Round(1120m * PricePerM("ImageOutputPerM", 30m) / 1_000_000m, 4);

    private decimal PricePerM(string name, decimal fallback) =>
        decimal.TryParse(_configuration[$"Ai:Pricing:{name}"], NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public async Task<TextResult> GenerateJsonAsync(string systemPrompt, string userPrompt, int maxOutputTokens, double temperature, CancellationToken ct)
    {
        var model = TextModel;
        var body = new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { parts = new[] { new { text = userPrompt } } } },
            generationConfig = new { temperature, maxOutputTokens, responseMimeType = "application/json" }
        };

        var (doc, key) = await SendAsync(model, body, ct);
        using (doc)
        {
            var root = doc.RootElement;
            var (input, output) = ReadUsage(root);
            var cost = _meter.Cost(false, input, output, key.IsFree);
            await _meter.RecordAsync("film-script", model, key.IsFree, root.TryGetProperty("usageMetadata", out var u) ? u.Clone() : null);

            ThrowIfBlocked(root);
            var candidate = root.GetProperty("candidates")[0];
            var finish = candidate.TryGetProperty("finishReason", out var fr) ? fr.GetString() : null;
            string? text = null;
            if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                {
                    var isThought = part.TryGetProperty("thought", out var th) && th.ValueKind == JsonValueKind.True;
                    if (!isThought && part.TryGetProperty("text", out var t)) text = t.GetString();
                }
            }
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException($"Gemini không trả về nội dung (finishReason: {finish ?? "?"}).");
            if (finish == "MAX_TOKENS")
                throw new InvalidOperationException("Gemini trả lời bị cắt giữa chừng (vượt giới hạn token). Thử lại.");

            return new TextResult(StripCodeFence(text), cost, key.IsFree, input + output);
        }
    }

    public async Task<ImageResult> GenerateImageAsync(string prompt, string aspectRatio, IReadOnlyList<ReferenceImage> references, CancellationToken ct)
    {
        var model = ImageModel;
        var parts = new List<object>();
        foreach (var r in references)
        {
            parts.Add(new { inline_data = new { mime_type = r.MimeType, data = Convert.ToBase64String(r.Bytes) } });
        }
        parts.Add(new { text = prompt });

        var body = new
        {
            contents = new[] { new { parts } },
            generationConfig = new
            {
                responseModalities = new[] { "IMAGE" },
                imageConfig = new { aspectRatio }
            }
        };

        var (doc, key) = await SendAsync(model, body, ct);
        using (doc)
        {
            var root = doc.RootElement;
            var (input, output) = ReadUsage(root);
            var cost = key.IsFree ? 0m : Math.Round(
                (input * PricePerM("TextInputPerM", 0.30m) + output * PricePerM("ImageOutputPerM", 30m)) / 1_000_000m, 6);

            ThrowIfBlocked(root);
            var candidate = root.GetProperty("candidates")[0];
            if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var ps))
            {
                foreach (var part in ps.EnumerateArray())
                {
                    if (part.TryGetProperty("inlineData", out var inline) && inline.TryGetProperty("data", out var data))
                    {
                        var bytes = Convert.FromBase64String(data.GetString() ?? string.Empty);
                        var mime = inline.TryGetProperty("mimeType", out var m) ? m.GetString() ?? "image/png" : "image/png";
                        await _meter.RecordAsync("film-image", model, key.IsFree, root.TryGetProperty("usageMetadata", out var u) ? u.Clone() : null, costOverrideUsd: cost);
                        return new ImageResult(bytes, mime, cost, key.IsFree, input + output);
                    }
                }
            }

            var finish = candidate.TryGetProperty("finishReason", out var fr) ? fr.GetString() : "?";
            throw new InvalidOperationException($"Gemini không trả về ảnh (finishReason: {finish}). Prompt có thể bị bộ lọc an toàn chặn.");
        }
    }

    private async Task<(JsonDocument Doc, GeminiKeys.Key Key)> SendAsync(string model, object body, CancellationToken ct)
    {
        var keys = GeminiKeys.InOrder(_configuration, model);
        if (keys.Count == 0)
            throw new InvalidOperationException("Chưa cấu hình Gemini API key (Ai__Gemini__FreeApiKey hoặc Ai__Gemini__ApiKey).");

        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={key.Value}";

            // One retry for transient server errors (5xx) — common on image models under load.
            for (var attempt = 1; ; attempt++)
            {
                using var response = await _http.PostAsJsonAsync(url, body, ct);
                var json = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode) return (JsonDocument.Parse(json), key);

                var reason = ExtractError(json);
                var code = (int)response.StatusCode;
                if (code >= 500 && attempt < 2)
                {
                    _logger.LogWarning("Gemini {Model} HTTP {Code}, retrying once: {Reason}", model, code, reason);
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                    continue;
                }

                if (key.IsFree) GeminiKeys.MarkFreeRefused(key, model, response.StatusCode, reason);
                if (key.IsFree && i < keys.Count - 1)
                {
                    _logger.LogInformation("Gemini free key ({Label}) refused for {Model} (HTTP {Code}); trying next key.", key.Label, model, code);
                    break;
                }
                throw new InvalidOperationException($"Gemini ({model}) từ chối yêu cầu — HTTP {code}: {reason}");
            }
        }
        throw new InvalidOperationException("Không gọi được Gemini.");
    }

    /// <summary>Gemini refused the request or its answer on safety grounds.</summary>
    public sealed class SafetyBlockedException : InvalidOperationException
    {
        public string Reason { get; }

        public SafetyBlockedException(string reason)
            : base($"Gemini từ chối nội dung vì bộ lọc an toàn ({reason}). Hãy làm nhẹ tình tiết (bạo lực, máu, nhân vật vị thành niên) rồi thử lại.")
        {
            Reason = reason;
        }
    }

    private static readonly string[] SafetyReasons = { "SAFETY", "PROHIBITED_CONTENT", "BLOCKLIST", "SPII", "IMAGE_SAFETY", "IMAGE_PROHIBITED_CONTENT", "OTHER" };

    /// <summary>Prompt blocked (no candidates, promptFeedback.blockReason) or answer stopped for a safety reason.</summary>
    private static void ThrowIfBlocked(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0)
        {
            var reason = root.TryGetProperty("promptFeedback", out var pf) && pf.TryGetProperty("blockReason", out var br)
                ? br.GetString() ?? "BLOCKED"
                : "NO_CANDIDATES";
            throw new SafetyBlockedException(reason);
        }
        var finish = cands[0].TryGetProperty("finishReason", out var fr) ? fr.GetString() ?? "" : "";
        var hasContent = cands[0].TryGetProperty("content", out var c) && c.TryGetProperty("parts", out _);
        if (SafetyReasons.Contains(finish) && !hasContent) throw new SafetyBlockedException(finish);
        if (finish is "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "SPII") throw new SafetyBlockedException(finish);
    }

    private static (long Input, long Output) ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var u)) return (0, 0);
        long Read(string n) => u.TryGetProperty(n, out var v) && v.TryGetInt64(out var x) ? x : 0;
        return (Read("promptTokenCount"), Read("candidatesTokenCount") + Read("thoughtsTokenCount"));
    }

    private static string ExtractError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "Lỗi không rõ";
        }
        catch (JsonException) { }
        return json.Length > 300 ? json[..300] : json;
    }

    private static string StripCodeFence(string text)
    {
        var t = text.Trim();
        if (!t.StartsWith("```")) return t;
        var firstNewline = t.IndexOf('\n');
        var lastFence = t.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline > 0 && lastFence > firstNewline ? t[(firstNewline + 1)..lastFence].Trim() : t;
    }
}
