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
/// It also writes each situation's place as the camera sees it (the same words for one place) and, for a strict theme
/// (<see cref="AiStudioTopic.Locale"/>), translates the bibles' people and places of a foreign reference into the theme's
/// country and era — clothes, houses, vehicles, objects, writing.
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
            s.ReviewedSetting = string.IsNullOrWhiteSpace(a.Setting) ? null : Truncate(a.Setting.Trim(), MaxReviewChars);
        }
        // a strict theme: the people and places of the bibles translated into the theme's country and era, in place —
        // the user sees (and can still edit) the new clothes and places in the bibles
        if (AiStudioTopics.Find(p.Topic)?.Locale != null)
        {
            p.CharacterBibleJson = ApplyLocalized(p.CharacterBibleJson, json, "characters", CharacterFields);
            p.EnvironmentBibleJson = ApplyLocalized(p.EnvironmentBibleJson, json, "environments", EnvironmentFields);
        }
        await _db.SaveChangesAsync(ct);
    }

    private static readonly string[] CharacterFields = { "apparent_ethnicity", "skin_tone", "hair", "clothing", "footwear", "accessories" };
    private static readonly string[] EnvironmentFields = { "location", "layout", "background", "important_objects" };

    public static (string System, string User) BuildLogicPrompt(AiStudioProject p, IReadOnlyList<AiStudioScene> scenes)
    {
        var topic = AiStudioTopics.Find(p.Topic);
        var strict = topic?.Locale != null;
        var system =
            "You are the continuity supervisor and physical-plausibility editor for an AI text-to-video model (Google Veo). " +
            "Video models make illogical clips when an action is vague: hands go into fire or boiling pots, food passes through cheeks, " +
            "a child does something dangerous, feet slide or float while walking, objects appear, vanish, merge into hands or change size, " +
            "tools are held the wrong way, people pass through things or sit on nothing, limbs multiply, animals morph, extra people pop in, " +
            "cause and effect is broken, the place changes from shot to shot. You prevent that for EVERY kind of scene. " +
            (strict
                ? "You are also the production designer and costume designer of a strictly local film: the reference video may come from " +
                  "another country (often China), and you translate every person, garment, building, vehicle, tool, food and piece of writing " +
                  "into its authentic local equivalent, so that nothing foreign is left. "
                : "") +
            "Answer with valid JSON only.";
        var sb = new StringBuilder();
        sb.AppendLine($"Video: \"{p.Title}\"" + (topic != null ? $" — theme: {topic.Label} ({topic.Look}; {topic.SettingHint})" : ""));
        if (strict) sb.AppendLine($"The film must be set entirely in {topic!.Locale}");
        var cast = ParseArr(p.CharacterBibleJson).OfType<JsonObject>().ToList();
        var places = ParseArr(p.EnvironmentBibleJson).OfType<JsonObject>().ToList();
        if (strict && cast.Count > 0)
        {
            sb.AppendLine("Characters of the reference (bible):");
            foreach (var c in cast)
                sb.AppendLine($"  {AiStudioPromptEngine.Field(c, "id") ?? "?"}: " + AiStudioPromptEngine.Join("; ",
                    AiStudioPromptEngine.Field(c, "label"), AiStudioPromptEngine.Field(c, "gender"), AiStudioPromptEngine.Field(c, "age_group"),
                    AiStudioPromptEngine.Field(c, "hair") is { } h ? $"hair: {h}" : null,
                    AiStudioPromptEngine.Field(c, "clothing") is { } cl ? $"clothes: {cl}" : null,
                    AiStudioPromptEngine.Field(c, "footwear") is { } f ? $"footwear: {f}" : null,
                    AiStudioPromptEngine.Field(c, "accessories") is { } ac ? $"accessories: {ac}" : null));
        }
        if (strict && places.Count > 0)
        {
            sb.AppendLine("Places of the reference (bible):");
            foreach (var e in places)
                sb.AppendLine($"  {AiStudioPromptEngine.Field(e, "id") ?? "?"}: " + AiStudioPromptEngine.Join("; ",
                    AiStudioPromptEngine.Field(e, "location"), AiStudioPromptEngine.Field(e, "layout"),
                    AiStudioPromptEngine.Field(e, "background") is { } b ? $"background: {b}" : null,
                    AiStudioPromptEngine.Field(e, "important_objects") is { } o ? $"objects: {o}" : null));
        }
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
            var inCast = cast.Where(c => InSceneOf(c, s.Index)).Select(c => AiStudioPromptEngine.Field(c, "id")).Where(x => x != null).ToList();
            var inPlace = places.FirstOrDefault(e => InSceneOf(e, s.Index)) is { } pl ? AiStudioPromptEngine.Field(pl, "id") : null;
            sb.AppendLine($"Scene {s.Order + 1} ({s.TargetSeconds}s clip{(s.IsContinuation ? ", continues the previous shot" : "")}" +
                          (inPlace != null ? $", place {inPlace}" : "") + (inCast.Count > 0 ? $", cast {string.Join("+", inCast)}" : "") + "):");
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
                      "anything complex. Say exactly how many people are in frame and who they are, where each one is, body position, what each " +
                      "hand holds and how (a tool by its handle, the right end forward), the distance to anything hot, sharp, deep or heavy, the " +
                      "direction of movement, the start and the end of the action and its result (cause → effect). A scene that continues the " +
                      "previous one starts exactly where it ended (same position, same objects in the same hands). If the description is " +
                      "illogical, dangerous or impossible, use the closest everyday, safe version that keeps the meaning.");
        sb.AppendLine("- \"rules\": 2–4 short, concrete constraints for THIS action against the typical AI-video failures it invites " +
                      "(e.g. 'the rice goes into the mouth, not through the cheek', 'the bucket stays tied to the rope', 'feet stay on the ground " +
                      "while walking', 'the buffalo keeps four legs and its size', 'the sampan floats on the water, the paddle dips into it').");
        sb.AppendLine("- \"setting\": 1–2 sentences: the exact place of this situation as the camera sees it — the foreground, midground and " +
                      "background, the props this action needs placed where they are used, the time of day and the light. It must clearly fit the " +
                      "story moment (a lesson shows the classroom, a meal the table or kitchen, a trip the canal or the path…). Scenes in the same " +
                      "place use the same words for it, so the place does not change from shot to shot.");
        if (strict)
        {
            sb.AppendLine("Translate everything into the theme's place and era: every person, garment, house, vehicle, tool, food and decoration " +
                          "becomes its authentic local equivalent; keep the role, age, gender, mood and the story. " + topic!.WritingRule +
                          " Prefer simple numbers or no writing at all on chalkboards, books and signs.");
            if (cast.Count > 0)
                sb.AppendLine("- \"characters\": for EACH character id above: {\"id\", \"apparent_ethnicity\", \"skin_tone\", \"hair\", \"clothing\", " +
                              "\"footwear\", \"accessories\"} — the same person, dressed and looking as an authentic local person of the theme.");
            if (places.Count > 0)
                sb.AppendLine("- \"environments\": for EACH place id above: {\"id\", \"location\", \"layout\", \"background\", \"important_objects\"} — " +
                              "the same kind of place (a kitchen stays a kitchen, a school stays a school) as it really looks in the theme's place and era.");
        }
        else sb.AppendLine("Do not change who the people are, their clothes, the place, the era or the camera.");
        sb.AppendLine("No names, no on-screen text or subtitles.");
        sb.AppendLine("Return: {" + (strict ? "\"characters\":[…],\"environments\":[…]," : "") +
                      "\"scenes\":[{\"scene\": number, \"action\": \"...\", \"rules\": \"...\", \"setting\": \"...\"}]}");
        return (system, sb.ToString());
    }

    private static bool InSceneOf(JsonObject o, int index) =>
        o["scenes"] is JsonArray arr && arr.Any(x => x is JsonValue jv && jv.TryGetValue<double>(out var n) && (int)n == index);

    public sealed record LogicScene(string Action, string Rules, string Setting);

    public static Dictionary<int, LogicScene> ParseLogic(string json)
    {
        var result = new Dictionary<int, LogicScene>();
        try
        {
            if (JsonNode.Parse(json)?["scenes"] is not JsonArray arr) return result;
            foreach (var x in arr.OfType<JsonObject>())
            {
                if (x["scene"] is not JsonValue sv || !sv.TryGetValue<int>(out var n)) continue;
                result[n] = new LogicScene(Text(x["action"]), Text(x["rules"]), Text(x["setting"]));
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException) { result.Clear(); }
        return result;
    }

    /// <summary>A string, or a list of strings joined with "; ".</summary>
    private static string Text(JsonNode? node) => node switch
    {
        JsonArray list => string.Join("; ", list.Select(r => r?.ToString().Trim().TrimEnd('.')).Where(r => !string.IsNullOrEmpty(r))),
        JsonValue one => one.ToString(),
        _ => ""
    };

    /// <summary>
    /// Writes the review's local version of each bible entry (by id) over its fields and marks it "localized"; the other
    /// fields (age, build, the scenes it is in…) are kept. Entries the review left out stay as they were.
    /// </summary>
    public static string ApplyLocalized(string bibleJson, string reviewJson, string key, IReadOnlyList<string> fields)
    {
        var bible = ParseArr(bibleJson);
        JsonArray? answers;
        try { answers = JsonNode.Parse(reviewJson)?[key] as JsonArray; }
        catch (System.Text.Json.JsonException) { return bibleJson; }
        if (answers == null) return bibleJson;
        foreach (var entry in bible.OfType<JsonObject>())
        {
            var id = AiStudioPromptEngine.Field(entry, "id");
            if (id == null) continue;
            var local = answers.OfType<JsonObject>().FirstOrDefault(a => string.Equals(AiStudioPromptEngine.Field(a, "id"), id, StringComparison.OrdinalIgnoreCase));
            if (local == null) continue;
            var changed = false;
            foreach (var f in fields)
            {
                var value = Text(local[f]).Trim();
                if (AiStudioPromptEngine.Clean(value) == null) continue;
                entry[f] = Truncate(value, 600);
                changed = true;
            }
            if (changed) entry["localized"] = true;
        }
        return bible.ToJsonString(Readable);
    }

    private static readonly System.Text.Json.JsonSerializerOptions Readable = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

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
