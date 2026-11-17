using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Enums;
using NewsStudio.Infrastructure.Common;

namespace NewsStudio.Infrastructure.Ai;

public class GeminiAiEditorService : IAIEditorService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly AiUsageMeter _meter;
    private readonly ILogger<GeminiAiEditorService> _logger;

    public AiProviderType ProviderType => AiProviderType.Gemini;

    public GeminiAiEditorService(
        HttpClient httpClient,
        IConfiguration configuration,
        AiUsageMeter meter,
        ILogger<GeminiAiEditorService> logger)
    {
        _meter = meter;
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<EditorialScriptResult> GenerateEditorialScriptAsync(
        EditorialScriptPromptInput input,
        string? modelName = null,
        CancellationToken cancellationToken = default)
    {
        var model = !string.IsNullOrWhiteSpace(modelName)
            ? modelName
            : _configuration["Ai:Gemini:Model"] ?? "gemini-flash-latest";

        _logger.LogInformation("GeminiAiEditorService generating script for project {ProjectId} with model {Model}",
            input.ProjectId, model);

        var simulate = SimulationMode.IsEnabled(_configuration);

        if (!GeminiKeys.HasAny(_configuration))
        {
            _logger.LogInformation("No Gemini API key configured. Using local editorial template script engine.");
            return GenerateDeterministicEditorialScript(input, "Gemini", model);
        }

        try
        {
            return await CallGeminiApiAsync(input, model, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (simulate)
            {
                _logger.LogWarning(ex, "Gemini API call failed. Simulation mode: using local template script.");
                return GenerateDeterministicEditorialScript(input, "Gemini", model);
            }
            _logger.LogError(ex, "Gemini API call failed for project {ProjectId}", input.ProjectId);
            throw new InvalidOperationException($"Gọi Gemini thất bại: {ex.Message}", ex);
        }
    }

    private async Task<EditorialScriptResult> CallGeminiApiAsync(
        EditorialScriptPromptInput input,
        string model,
        CancellationToken cancellationToken)
    {
        var userPrompt = PromptTemplates.BuildUserPrompt(
            input.ArticleTitle,
            input.Publisher,
            input.Author,
            input.PublishedDate,
            input.CanonicalUrl,
            input.ContentBody,
            input.AvailableImages.Count,
            input.TargetDurationSeconds,
            input.Tone);

        var requestBody = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = PromptTemplates.SystemEditorialPrompt } }
            },
            contents = new[]
            {
                new { parts = new[] { new { text = userPrompt } } }
            },
            generationConfig = new
            {
                temperature = 0.4,
                // Gemini 2.5+ "thinking" tokens count against this limit; 2048 can cut the JSON short.
                maxOutputTokens = 8192,
                responseMimeType = "application/json"
            }
        };

        var (response, usedKey) = await SendWithKeysAsync(model, requestBody, cancellationToken);

        using var okResponse = response;
        var jsonDoc = await okResponse.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
        if (jsonDoc == null) throw new InvalidOperationException("Empty response from Gemini API");

        var text = jsonDoc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "{}";

        await _meter.RecordAsync("script", model, usedKey.IsFree,
            jsonDoc.RootElement.TryGetProperty("usageMetadata", out var meta) ? meta.Clone() : null, projectId: input.ProjectId);
        int totalTokens = 0;
        if (jsonDoc.RootElement.TryGetProperty("usageMetadata", out var usage) &&
            usage.TryGetProperty("totalTokenCount", out var totalTokenCount))
        {
            totalTokens = totalTokenCount.GetInt32();
        }

        var parsed = ParseEditorialJson(text, input);
        parsed.AiProvider = "Gemini";
        parsed.ModelName = model;
        parsed.PromptVersion = PromptTemplates.PromptVersion;
        parsed.TokenUsage = totalTokens > 0 ? totalTokens : CountTokensApprox(userPrompt + text);
        // Approx Gemini Flash cost: $0.075 / 1M input, $0.30 / 1M output (~$0.0002 / run)
        parsed.EstimatedCostUsd = usedKey.IsFree ? 0m : Math.Round((decimal)parsed.TokenUsage * 0.0000002m, 6);
        _logger.LogInformation("Gemini script for project {ProjectId} used the {KeyType} key ({Tokens} tokens)",
            input.ProjectId, usedKey.Label, parsed.TokenUsage);

        return parsed;
    }

    /// <summary>
    /// Free key first; when Google refuses it (quota used up etc.) the same request goes to the paid key.
    /// Returns the successful response (caller disposes it) or throws with Google's own explanation.
    /// </summary>
    private async Task<(HttpResponseMessage Response, GeminiKeys.Key Key)> SendWithKeysAsync(string model, object requestBody, CancellationToken cancellationToken)
    {
        var keys = GeminiKeys.InOrder(_configuration, model);
        HttpResponseMessage? response = null;
        for (var i = 0; i < keys.Count; i++)
        {
            var usedKey = keys[i];
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={usedKey.Value}";
            response?.Dispose();
            response = await _httpClient.PostAsJsonAsync(url, requestBody, cancellationToken);
            if (response.IsSuccessStatusCode) return (response, usedKey);

            // Keep Google's own explanation — a bare "404" hides that the model name is wrong/retired.
            var reason = await ReadGoogleErrorAsync(response, cancellationToken);
            if (usedKey.IsFree && i < keys.Count - 1)
            {
                GeminiKeys.MarkFreeRefused(model, response.StatusCode, reason);
                _logger.LogInformation("Gemini free key refused (HTTP {Status}) for {Model}; using paid key. {Reason}",
                    (int)response.StatusCode, model, reason);
                continue;
            }
            if (usedKey.IsFree) GeminiKeys.MarkFreeRefused(model, response.StatusCode, reason);

            var hint = response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? $" Model \"{model}\" không dùng được với API key này — xem danh sách model khả dụng trong Cài đặt rồi đặt lại Ai:Gemini:Model."
                : (int)response.StatusCode == 429 && usedKey.IsFree
                    ? " Key miễn phí đã hết lượt — thêm key trả phí (Ai__Gemini__ApiKey) để tự chuyển sang khi hết lượt free."
                    : string.Empty;
            var status = response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"HTTP {(int)status}: {reason}.{hint}", null, status);
        }
        throw new InvalidOperationException("Chưa cấu hình Gemini API key.");
    }

    /// <summary>
    /// One JSON-mode Gemini call (system + user prompt) returning the model's raw JSON text.
    /// Throws when no key is configured or Google refuses — never a made-up answer.
    /// </summary>
    public async Task<string> GenerateJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default, string kind = "ai", Guid? projectId = null)
    {
        if (!GeminiKeys.HasAny(_configuration))
            throw new InvalidOperationException("Chưa cấu hình Gemini API key (Ai__Gemini__FreeApiKey hoặc Ai__Gemini__ApiKey).");

        var model = _configuration["Ai:Gemini:Model"] ?? "gemini-flash-latest";
        var requestBody = new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { parts = new[] { new { text = userPrompt } } } },
            generationConfig = new { temperature = 0.3, maxOutputTokens = 8192, responseMimeType = "application/json" }
        };
        var (response, usedKey) = await SendWithKeysAsync(model, requestBody, cancellationToken);
        using (response)
        {
            var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken)
                      ?? throw new InvalidOperationException("Gemini trả về rỗng.");
            await _meter.RecordAsync(kind, model, usedKey.IsFree,
                doc.RootElement.TryGetProperty("usageMetadata", out var usage) ? usage.Clone() : null, projectId: projectId);
            var parts = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");
            // Thinking models may put thoughts in earlier parts; the answer is the last text part
            string? text = null;
            foreach (var part in parts.EnumerateArray())
                if (part.TryGetProperty("text", out var t) && !(part.TryGetProperty("thought", out var th) && th.ValueKind == JsonValueKind.True))
                    text = t.GetString();
            _logger.LogInformation("Gemini JSON call ({Model}) used the {KeyType} key", model, usedKey.Label);
            return text ?? throw new InvalidOperationException("Gemini không trả về nội dung.");
        }
    }

    public static EditorialScriptResult GenerateDeterministicEditorialScript(
        EditorialScriptPromptInput input,
        string provider,
        string model)
    {
        // Extract key sentences and summarize cleanly
        var sentences = input.ContentBody
            .Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 20)
            .ToList();

        var firstPoint = sentences.Count > 0 ? sentences[0] : input.ArticleTitle;
        var secondPoint = sentences.Count > 1 ? sentences[1] : "Sự kiện đang thu hút sự quan tâm lớn của dư luận.";
        var thirdPoint = sentences.Count > 2 ? sentences[2] : "Các chuyên gia đánh giá đây là bước chuyển biến đáng chú ý.";

        var cleanTitle = input.ArticleTitle.Length > 60
            ? input.ArticleTitle.Substring(0, 57) + "..."
            : input.ArticleTitle;

        var videoTitle = $"TIÊU ĐIỂM: {cleanTitle}";
        var hook = $"Chuyện gì đang xảy ra với {input.ArticleTitle.ToLowerInvariant()}? Những diễn biến mới nhất vừa được công bố!";

        var sourceAttribution = !string.IsNullOrWhiteSpace(input.Publisher)
            ? $"Theo nguồn tin từ {input.Publisher}"
            : "Theo thông tin báo chí chính thức";

        var keyFacts = $"- Dữ kiện 1: {firstPoint}.\n- Dữ kiện 2: {secondPoint}.\n- Thời điểm ghi nhận: {input.PublishedDate?.ToString("dd/MM/yyyy") ?? "Mới nhất"}.";

        var context = $"Sự việc diễn ra trong bối cảnh các vấn đề liên quan đang nhận được sự theo dõi sát sao từ cơ quan chức năng và công chúng.";

        var analysis = $"Dưới góc nhìn phân tích, động thái này không chỉ tác động trước mắt mà còn mở ra xu hướng dài hạn cho toàn bộ lĩnh vực liên quan.";

        var conclusion = $"Bạn đánh giá thế nào về diễn biến này? Hãy để lại ý kiến thảo luận ở phần bình luận bên dưới!";

        // Voice-over script designed for 55-65s reading speed (~125-145 words)
        var voiceOverScript = $"{hook} {firstPoint}. {secondPoint}. {thirdPoint}. {context} {analysis} {conclusion} {sourceAttribution}.";

        var words = voiceOverScript.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        // Standard Vietnamese reading speed: ~2.3 words/second -> ~55-65 seconds (target 60s)
        var estimatedSeconds = Math.Max(1, (int)Math.Round(words / PromptTemplates.WordsPerSecond));

        return new EditorialScriptResult
        {
            VideoTitle = videoTitle,
            Hook = hook,
            VoiceOverScript = voiceOverScript,
            KeyFacts = keyFacts,
            Context = context,
            Analysis = analysis,
            Conclusion = conclusion,
            SourceAttribution = sourceAttribution,
            WordCount = words,
            EstimatedReadingSeconds = estimatedSeconds,
            AiProvider = provider,
            ModelName = model,
            PromptVersion = PromptTemplates.PromptVersion,
            TokenUsage = 520,
            EstimatedCostUsd = 0.000104m
        };
    }

    private static EditorialScriptResult ParseEditorialJson(string jsonText, EditorialScriptPromptInput input)
    {
        try
        {
            // Remove markdown code block fences if present
            var clean = jsonText.Trim();
            if (clean.StartsWith("```json")) clean = clean.Substring(7);
            if (clean.StartsWith("```")) clean = clean.Substring(3);
            if (clean.EndsWith("```")) clean = clean.Substring(0, clean.Length - 3);
            clean = clean.Trim();

            using var doc = JsonDocument.Parse(clean);
            var root = doc.RootElement;

            var title = GetStringProperty(root, "videoTitle", input.ArticleTitle);
            var hook = GetStringProperty(root, "hook", "");
            var voiceOver = GetStringProperty(root, "voiceOverScript", "");
            var keyFacts = GetStringProperty(root, "keyFacts", "");
            var context = GetStringProperty(root, "context", "");
            var analysis = GetStringProperty(root, "analysis", "");
            var conclusion = GetStringProperty(root, "conclusion", "");
            var attribution = GetStringProperty(root, "sourceAttribution", $"Theo {input.Publisher}");

            if (string.IsNullOrWhiteSpace(voiceOver))
            {
                throw new InvalidOperationException("AI trả về kịch bản rỗng. Hãy thử viết lại.");
            }

            var words = voiceOver.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            // Vietnamese reading speed ≈ 2.3 words/second — report the real estimate, not a clamped one
            var estimatedSeconds = Math.Max(1, (int)Math.Round(words / 2.3));

            return new EditorialScriptResult
            {
                VideoTitle = title,
                Hook = hook,
                VoiceOverScript = voiceOver,
                KeyFacts = keyFacts,
                Context = context,
                Analysis = analysis,
                Conclusion = conclusion,
                SourceAttribution = attribution,
                FactSheetJson = JsonSerializer.Serialize(FactCheck.ParseFactSheet(root)),
                WordCount = words,
                EstimatedReadingSeconds = estimatedSeconds
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("AI trả về kịch bản không đúng định dạng JSON. Hãy thử viết lại.", ex);
        }
    }

    private static string GetStringProperty(JsonElement element, string propName, string defaultValue)
    {
        if (element.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var str = prop.GetString();
            if (!string.IsNullOrWhiteSpace(str)) return str.Trim();
        }
        return defaultValue;
    }

    private static async Task<string> ReadGoogleErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
            {
                return msg.GetString() ?? response.ReasonPhrase ?? "Lỗi không rõ";
            }
        }
        catch (JsonException)
        {
            // fall through
        }
        return string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase ?? "Lỗi không rõ" : body[..Math.Min(body.Length, 300)];
    }

    /// <summary>
    /// Lists the Gemini models this API key can use for generateContent (live from Google, not hard-coded).
    /// Returns null when no key is configured.
    /// </summary>
    public async Task<List<AiModelInfoDto>?> ListAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = GeminiKeys.ForListing(_configuration);
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var result = new List<AiModelInfoDto>();
        string? pageToken = null;
        do
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000&key={apiKey}"
                      + (pageToken != null ? $"&pageToken={Uri.EscapeDataString(pageToken)}" : string.Empty);
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var reason = await ReadGoogleErrorAsync(response, cancellationToken);
                throw new InvalidOperationException($"Không lấy được danh sách model Gemini: {reason}");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (doc.RootElement.TryGetProperty("models", out var models))
            {
                foreach (var m in models.EnumerateArray())
                {
                    var supportsGenerate = m.TryGetProperty("supportedGenerationMethods", out var methods)
                        && methods.EnumerateArray().Any(x => x.GetString() == "generateContent");
                    if (!supportsGenerate) continue;

                    var name = m.GetProperty("name").GetString() ?? string.Empty;
                    result.Add(new AiModelInfoDto
                    {
                        Id = name.StartsWith("models/") ? name["models/".Length..] : name,
                        DisplayName = m.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? name : name
                    });
                }
            }
            pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var nt) ? nt.GetString() : null;
        } while (!string.IsNullOrEmpty(pageToken));

        return result;
    }

    private static int CountTokensApprox(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return (int)Math.Ceiling(text.Length / 3.5);
    }
}
