using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Infrastructure.Ai;

public class ClaudeAiEditorService : IAIEditorService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ClaudeAiEditorService> _logger;

    public AiProviderType ProviderType => AiProviderType.Claude;

    public ClaudeAiEditorService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ClaudeAiEditorService> logger)
    {
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
            : _configuration["Ai:Claude:Model"] ?? "claude-3-5-haiku-20241022";

        var apiKey = _configuration["Ai:Claude:ApiKey"]
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        _logger.LogInformation("ClaudeAiEditorService generating script for project {ProjectId} with model {Model}",
            input.ProjectId, model);

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                return await CallClaudeApiAsync(input, model, apiKey, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Claude API call failed or timed out. Falling back to local editorial synthesis engine.");
            }
        }
        else
        {
            _logger.LogInformation("No Claude API key configured. Using built-in editorial synthesis engine.");
        }

        return GeminiAiEditorService.GenerateDeterministicEditorialScript(input, "Claude", model);
    }

    private async Task<EditorialScriptResult> CallClaudeApiAsync(
        EditorialScriptPromptInput input,
        string model,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var url = "https://api.anthropic.com/v1/messages";
        var userPrompt = PromptTemplates.BuildUserPrompt(
            input.ArticleTitle,
            input.Publisher,
            input.Author,
            input.PublishedDate,
            input.CanonicalUrl,
            input.ContentBody,
            input.AvailableImages.Count,
            input.TargetDurationSeconds);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var requestBody = new
        {
            model = model,
            max_tokens = 2048,
            system = PromptTemplates.SystemEditorialPrompt,
            messages = new[]
            {
                new { role = "user", content = userPrompt }
            }
        };

        request.Content = JsonContent.Create(requestBody);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
        if (jsonDoc == null) throw new InvalidOperationException("Empty response from Claude API");

        var text = jsonDoc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "{}";

        int inputTokens = 0;
        int outputTokens = 0;
        if (jsonDoc.RootElement.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var inTok)) inputTokens = inTok.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var outTok)) outputTokens = outTok.GetInt32();
        }

        var totalTokens = inputTokens + outputTokens;
        var parsed = ParseClaudeJson(text, input);
        parsed.AiProvider = "Claude";
        parsed.ModelName = model;
        parsed.PromptVersion = PromptTemplates.PromptVersion;
        parsed.TokenUsage = totalTokens > 0 ? totalTokens : 600;
        // Claude 3.5 Haiku: $0.80/M input, $4.00/M output (~$0.0012/run)
        parsed.EstimatedCostUsd = Math.Round((inputTokens * 0.0000008m) + (outputTokens * 0.000004m), 6);

        return parsed;
    }

    private static EditorialScriptResult ParseClaudeJson(string jsonText, EditorialScriptPromptInput input)
    {
        try
        {
            var clean = jsonText.Trim();
            if (clean.StartsWith("```json")) clean = clean.Substring(7);
            if (clean.StartsWith("```")) clean = clean.Substring(3);
            if (clean.EndsWith("```")) clean = clean.Substring(0, clean.Length - 3);
            clean = clean.Trim();

            using var doc = JsonDocument.Parse(clean);
            var root = doc.RootElement;

            var title = GetStringProperty(root, "videoTitle", input.ArticleTitle);
            var hook = GetStringProperty(root, "hook", "Điểm nóng tin tức hôm nay bạn không thể bỏ lỡ!");
            var voiceOver = GetStringProperty(root, "voiceOverScript", "");
            var keyFacts = GetStringProperty(root, "keyFacts", "");
            var context = GetStringProperty(root, "context", "");
            var analysis = GetStringProperty(root, "analysis", "");
            var conclusion = GetStringProperty(root, "conclusion", "Bạn nghĩ sao về thông tin này?");
            var attribution = GetStringProperty(root, "sourceAttribution", $"Theo {input.Publisher}");

            if (string.IsNullOrWhiteSpace(voiceOver))
            {
                voiceOver = $"{hook} {keyFacts} {analysis} {conclusion} {attribution}.";
            }

            var words = voiceOver.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            var estimatedSeconds = Math.Clamp((int)Math.Round(words / 2.3), 45, 60);

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
                WordCount = words,
                EstimatedReadingSeconds = estimatedSeconds
            };
        }
        catch
        {
            return GeminiAiEditorService.GenerateDeterministicEditorialScript(input, "Claude", "claude-3-5-haiku-20241022");
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
}
