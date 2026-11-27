using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Infrastructure.AiStudio;

/// <summary>
/// Logic review: before the prompts are written, every enabled scene's action (as the local model saw it) is read by
/// a "continuity supervisor" (Gemini, text only — never the video) that rewrites it as what can really and safely
/// happen in that clip — concrete body positions, what each hand holds, distances to anything dangerous, one main
/// action per few seconds — plus a few constraints against the failures a video model makes for THAT action. It is
/// general: whatever the scene shows (cooking, fishing, a buffalo ride, a game, a meal…), not a list of keywords.
/// </summary>
public partial class AiStudioService
{
    public const int MaxReviewChars = 900;

    public async Task<AiStudioProjectDto> ReviewLogicAsync(Guid id, CancellationToken ct)
    {
        var p = await EditableProjectAsync(id, ct);
        var scenes = await _db.AiStudioScenes.Where(s => s.ProjectId == id).OrderBy(s => s.Order).ToListAsync(ct);
        await ReviewLogicAsync(p, scenes, ct);
        await RebuildPromptsAsync(p, false, "engine", ct);
        return await GetProjectAsync(id, ct);
    }

    private async Task ReviewLogicAsync(AiStudioProject p, IReadOnlyList<AiStudioScene> scenes, CancellationToken ct)
    {
        var enabled = scenes.Where(s => s.Enabled).OrderBy(s => s.Order).ToList();
        if (enabled.Count == 0) throw new InvalidOperationException("Chưa có cảnh nào bật.");
        var (system, user) = BuildLogicPrompt(p, enabled);
        await _studio.EnsureMonthlyBudgetAsync(0.05m, ct);
        var (json, cost, tokens, model) = await _writer.JsonAsync(system, user, ct);
        _db.StudioCostRecords.Add(new StudioCostRecord
        {
            Kind = "Plan", Model = model, Tokens = tokens, CostUsd = cost, Note = $"AI Video Studio · {Short(p.Title)} · rà soát logic cảnh"
        });
        var answers = ParseLogic(json);
        if (answers.Count == 0)
        {
            await _db.SaveChangesAsync(ct);
            throw new InvalidOperationException("Gemini không trả về bản rà soát hợp lệ — bấm thử lại.");
        }
        foreach (var s in enabled)
        {
            if (!answers.TryGetValue(s.Order + 1, out var a) || string.IsNullOrWhiteSpace(a.Action)) continue;
            s.ReviewedAction = Truncate(a.Action.Trim(), MaxReviewChars);
            s.ReviewedRules = string.IsNullOrWhiteSpace(a.Rules) ? null : Truncate(a.Rules.Trim(), MaxReviewChars);
        }
        await _db.SaveChangesAsync(ct);
    }

    public static (string System, string User) BuildLogicPrompt(AiStudioProject p, IReadOnlyList<AiStudioScene> scenes)
    {
        var topic = AiStudioTopics.Find(p.Topic);
        var system =
            "You are the continuity supervisor and physical-plausibility editor for an AI text-to-video model (Seedance). " +
            "Video models make illogical clips when an action is vague: hands go into fire or boiling pots, food passes through cheeks, " +
            "a child does something dangerous, feet slide or float while walking, objects appear, vanish, merge into hands or change size, " +
            "people pass through things, limbs multiply, animals morph, cause and effect is broken. You prevent that for EVERY kind of scene. " +
            "Answer with valid JSON only.";
        var sb = new StringBuilder();
        sb.AppendLine($"Video: \"{p.Title}\"" + (topic != null ? $" — theme: {topic.Label} ({topic.Look}; {topic.SettingHint})" : ""));
        sb.AppendLine("Scenes in order (what the reference showed, as a local model described it — it can be vague or wrong):");
        foreach (var s in scenes)
        {
            var a = ParseObj(s.AnalysisJson);
            var actions = (a["timeline"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Select(t => AiStudioPromptEngine.Join(", ", AiStudioPromptEngine.Field(t, "action"), AiStudioPromptEngine.Field(t, "body")))
                .Where(x => x.Length > 0).ToList();
            var people = (a["characters"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Select(c => AiStudioPromptEngine.Join(" ", AiStudioPromptEngine.Field(c, "age_group"), AiStudioPromptEngine.Field(c, "label") ?? AiStudioPromptEngine.Field(c, "gender")))
                .Where(x => x.Length > 0).ToList();
            var env = a["environment"] as JsonObject;
            var objects = env?["objects"] is JsonArray arr ? string.Join(", ", arr.Select(x => x?.ToString()).Where(x => AiStudioPromptEngine.Clean(x) != null).Take(8)) : null;
            sb.AppendLine($"Scene {s.Order + 1} ({s.TargetSeconds}s clip{(s.IsContinuation ? ", continues the previous shot" : "")}):");
            if (AiStudioPromptEngine.Field(a, "summary") is { } sum) sb.AppendLine($"  summary: {sum}");
            if (actions.Count > 0) sb.AppendLine($"  actions: {string.Join("; ", actions)}");
            if (people.Count > 0) sb.AppendLine($"  people: {string.Join(", ", people)}");
            sb.AppendLine($"  place: {AiStudioPromptEngine.Field(env, "location") ?? "?"}" + (objects is { Length: > 0 } ? $"; objects: {objects}" : ""));
            if (AiStudioPromptEngine.Field(a, "object_interaction") is { } oi) sb.AppendLine($"  interaction: {oi}");
            if (AiStudioNarration.PromptText(s.Narration, s.NarrationEn, s.NarrationEdited) is { } story) sb.AppendLine($"  narration (story context): {story}");
        }
        sb.AppendLine();
        sb.AppendLine("For EACH scene write:");
        sb.AppendLine("- \"action\": what the clip must show, 1–3 short present-tense English sentences: concrete, physically possible, safe and " +
                      "sensible for the people involved (children above all), true to the story. One main action per ~3 seconds of clip — simplify " +
                      "anything complex. Say where each person is, body position, what each hand holds, the distance to anything hot, sharp, deep " +
                      "or heavy, the direction of movement, and the result of the action (cause → effect). If the description is illogical or " +
                      "dangerous, use the closest everyday, safe version that keeps the meaning.");
        sb.AppendLine("- \"rules\": 2–4 short, concrete constraints for THIS action against the typical AI-video failures it invites " +
                      "(e.g. 'the rice goes into the mouth, not through the cheek', 'the bucket stays tied to the rope', 'feet stay on the ground " +
                      "while walking', 'the buffalo keeps four legs and its size').");
        sb.AppendLine("Do not change who the people are, their clothes, the place, the era or the camera. No names, no text on screen.");
        sb.AppendLine("Return: {\"scenes\":[{\"scene\": number, \"action\": \"...\", \"rules\": \"...\"}]}");
        return (system, sb.ToString());
    }

    public static Dictionary<int, (string Action, string Rules)> ParseLogic(string json)
    {
        var result = new Dictionary<int, (string, string)>();
        try
        {
            if (JsonNode.Parse(json)?["scenes"] is not JsonArray arr) return result;
            foreach (var x in arr.OfType<JsonObject>())
            {
                if (x["scene"] is not JsonValue sv || !sv.TryGetValue<int>(out var n)) continue;
                var rules = x["rules"] switch
                {
                    JsonArray list => string.Join("; ", list.Select(r => r?.ToString().Trim().TrimEnd('.')).Where(r => !string.IsNullOrEmpty(r))),
                    { } one => one.ToString(),
                    _ => ""
                };
                result[n] = (x["action"]?.ToString() ?? "", rules);
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException) { result.Clear(); }
        return result;
    }

    /// <summary>At analysis / after condensing: the review, best effort (the page has a button to run it again).</summary>
    private async Task TryReviewLogicAsync(AiStudioProject p, IReadOnlyList<AiStudioScene> scenes, CancellationToken ct)
    {
        try { await ReviewLogicAsync(p, scenes, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AI Studio: logic review of {Project} not done", p.Id);
        }
    }
}
