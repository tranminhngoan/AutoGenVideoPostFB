using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.FilmFactory;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Infrastructure.AiStudio;

/// <summary>
/// How long the made video is: a reference up to a minute keeps its length (each scene its own length); a longer one
/// is condensed to a minute — every scene shortened in proportion (at least 3 s, so an action still reads), and when
/// that is still too long the least important scenes (short, silent, uncertain) are left out. The first scene stays.
/// </summary>
public static class AiStudioDuration
{
    public const int MaxVideoSeconds = 60;
    public const int MinCondensedSeconds = 3;

    public sealed record Slot(Guid Id, double Seconds, bool HasNarration, double Confidence);
    public sealed record Plan(Dictionary<Guid, int> Targets, List<Guid> Dropped, bool Condensed);

    public static int Natural(double seconds) => SeedanceVideoClient.ClampSeconds((int)Math.Round(seconds));

    public static Plan Fit(IReadOnlyList<Slot> slots, int max = MaxVideoSeconds)
    {
        var natural = slots.ToDictionary(x => x.Id, x => Natural(x.Seconds));
        if (natural.Values.Sum() <= max) return new Plan(natural, new List<Guid>(), false);

        var kept = slots.ToList();
        var dropped = new List<Guid>();
        while (true)
        {
            var total = Math.Max(0.1, kept.Sum(x => x.Seconds));
            var f = max / total;
            int Min(Slot x) => Math.Min(MinCondensedSeconds, natural[x.Id]);
            var targets = kept.ToDictionary(x => x.Id, x => Math.Clamp((int)Math.Round(x.Seconds * f), Min(x), natural[x.Id]));
            if (targets.Values.Sum() <= max || kept.Count == 1)
            {
                // the seconds left go to the scenes that lost the most, up to their own length
                for (var left = max - targets.Values.Sum(); left > 0; left--)
                {
                    var best = kept.Where(x => targets[x.Id] < natural[x.Id]).OrderByDescending(x => x.Seconds * f - targets[x.Id]).FirstOrDefault();
                    if (best == null) break;
                    targets[best.Id]++;
                }
                return new Plan(targets, dropped, true);
            }
            // too many scenes for a minute: leave out the least important one (never the first)
            var least = kept.Skip(1).OrderBy(x => x.Seconds + (x.HasNarration ? 2 : 0) + x.Confidence).First();
            kept.Remove(least);
            dropped.Add(least.Id);
        }
    }

    /// <summary>Seconds the narrator needs for these words at the reference speaker's pace (a little slower for the AI voice).</summary>
    public static double SpeechSeconds(string? text, double? syllablesPerSecond)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var sentences = Math.Max(1, text.Count(c => c is '.' or '!' or '?' or '…'));
        return words / Rate(syllablesPerSecond) + sentences * 0.35;
    }

    /// <summary>Vietnamese syllables (= words) a second the narrator reads: the reference pace, kept within 2.6–3.8.</summary>
    public static double Rate(double? syllablesPerSecond) => Math.Clamp((syllablesPerSecond ?? 3.4) * 0.92, 2.6, 3.8);
}

/// <summary>Writes JSON from text with Gemini (narration condensing) — the reference video is never sent.</summary>
public interface IAiStudioTextWriter
{
    Task<(string Json, decimal CostUsd, long Tokens, string Model)> JsonAsync(string system, string user, CancellationToken ct);
}

public sealed class GeminiTextWriter : IAiStudioTextWriter
{
    private readonly FilmGeminiClient _gemini;
    public GeminiTextWriter(FilmGeminiClient gemini) => _gemini = gemini;

    public async Task<(string Json, decimal CostUsd, long Tokens, string Model)> JsonAsync(string system, string user, CancellationToken ct)
    {
        var model = _gemini.TextModel;
        var result = await _gemini.GenerateJsonAsync(system, user, 8192, 0.4, ct, model, lightThinking: true);
        return (result.Text, result.CostUsd, result.Tokens, model);
    }
}

public partial class AiStudioService
{
    /// <summary>Applies the duration plan to the enabled scenes (lengths, scenes left out); true when it condensed.</summary>
    private static bool ApplyDurationPlan(IReadOnlyList<AiStudioScene> scenes)
    {
        var enabled = scenes.Where(s => s.Enabled).OrderBy(s => s.Order).ToList();
        var plan = AiStudioDuration.Fit(enabled.Select(s =>
            new AiStudioDuration.Slot(s.Id, s.EndSeconds - s.StartSeconds, !string.IsNullOrWhiteSpace(s.Narration), s.Confidence)).ToList());
        foreach (var s in enabled)
        {
            if (plan.Dropped.Contains(s.Id)) s.Enabled = false;
            else s.TargetSeconds = plan.Targets[s.Id];
        }
        return plan.Condensed;
    }

    /// <summary>
    /// "Rút gọn còn 1 phút": the scene lengths planned again from the reference (enabled scenes only), the prompts
    /// rebuilt for the new lengths (main actions only when shortened), then the narration shortened to fit.
    /// </summary>
    public async Task<AiStudioProjectDto> FitDurationAsync(Guid id, CancellationToken ct)
    {
        var p = await EditableProjectAsync(id, ct);
        var scenes = await _db.AiStudioScenes.Where(s => s.ProjectId == id).OrderBy(s => s.Order).ToListAsync(ct);
        ApplyDurationPlan(scenes);
        await _db.SaveChangesAsync(ct);
        await RebuildPromptsAsync(p, false, "engine", ct);
        await CondenseNarrationAsync(p, scenes, ct);
        return await GetProjectAsync(id, ct);
    }

    public async Task<AiStudioProjectDto> CondenseNarrationAsync(Guid id, CancellationToken ct)
    {
        var p = await EditableProjectAsync(id, ct);
        var scenes = await _db.AiStudioScenes.Where(s => s.ProjectId == id).OrderBy(s => s.Order).ToListAsync(ct);
        if (!await CondenseNarrationAsync(p, scenes, ct)) throw new InvalidOperationException("Lời bình đã vừa với độ dài video — không cần rút gọn.");
        return await GetProjectAsync(id, ct);
    }

    /// <summary>
    /// Shortens the narration of the enabled scenes to what the narrator can read in each clip (Gemini, text only),
    /// keeping the story and the storyteller's voice. False when it already fits.
    /// </summary>
    private async Task<bool> CondenseNarrationAsync(AiStudioProject p, IReadOnlyList<AiStudioScene> scenes, CancellationToken ct)
    {
        var rate = AiStudioDuration.Rate(AiStudioNarration.SpeakerOf(p.TranscriptJson)?.SyllablesPerSecond);
        var spoken = scenes.Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.Narration)).OrderBy(s => s.Order).ToList();
        if (spoken.Count == 0) return false;
        var budget = spoken.ToDictionary(s => s.Id, s => Math.Max(0, (int)Math.Floor((s.TargetSeconds - 0.4) * rate)));
        int Words(string? t) => t?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length ?? 0;
        var video = scenes.Where(s => s.Enabled).Sum(s => s.TargetSeconds);
        var speech = AiStudioDuration.SpeechSeconds(FullNarration(scenes), AiStudioNarration.SpeakerOf(p.TranscriptJson)?.SyllablesPerSecond);
        if (speech <= video * 0.95) return false;

        var (system, user) = BuildCondensePrompt(p, spoken, budget);
        await _studio.EnsureMonthlyBudgetAsync(0.02m, ct);
        var (json, cost, tokens, model) = await _writer.JsonAsync(system, user, ct);
        _db.StudioCostRecords.Add(new StudioCostRecord
        {
            Kind = "Plan", Model = model, Tokens = tokens, CostUsd = cost, Note = $"AI Video Studio · {Short(p.Title)} · rút gọn lời bình"
        });
        var answers = ParseCondensed(json);
        if (answers.Count == 0)
        {
            await _db.SaveChangesAsync(ct);
            throw new InvalidOperationException("Gemini không trả về lời bình rút gọn hợp lệ — bấm thử lại.");
        }
        foreach (var s in spoken)
        {
            if (!answers.TryGetValue(s.Order + 1, out var a)) continue;
            s.Narration = string.IsNullOrWhiteSpace(a.Vi) ? null : Truncate(a.Vi.Trim(), 2000);
            s.NarrationEn = string.IsNullOrWhiteSpace(a.En) || s.Narration == null ? null : Truncate(a.En.Trim(), 2000);
            s.NarrationEdited = false;
        }
        await _db.SaveChangesAsync(ct);
        await RebuildPromptsAsync(p, false, "engine", ct); // the prompts quote the new words
        return true;
    }

    public static (string System, string User) BuildCondensePrompt(AiStudioProject p, IReadOnlyList<AiStudioScene> spoken, IReadOnlyDictionary<Guid, int> budget)
    {
        var system = "Bạn là biên tập viên lời bình cho video ngắn tiếng Việt. Bạn rút gọn lời bình cho vừa thời lượng mà vẫn giữ trọn câu chuyện. Chỉ trả về JSON hợp lệ.";
        var lines = string.Join("\n", spoken.Select(s => $"Cảnh {s.Order + 1} (tối đa {budget[s.Id]} chữ): \"{s.Narration!.Trim().Replace('"', '\'')}\""));
        var user =
            $"Video \"{p.Title}\" được rút gọn còn khoảng {AiStudioDuration.MaxVideoSeconds} giây nên lời bình mỗi cảnh phải ngắn lại.\n" +
            "Lời bình gốc theo từng cảnh (một chữ = một tiếng):\n" + lines + "\n\n" +
            "Yêu cầu:\n" +
            "- Viết lại lời bình từng cảnh KHÔNG vượt số chữ cho phép của cảnh đó.\n" +
            "- Giữ ý chính và mạch kể nối liền giữa các cảnh; câu trọn vẹn, tự nhiên như người kể chuyện.\n" +
            "- Giữ cách xưng hô, từ ngữ địa phương và giọng văn của lời gốc; không thêm chi tiết mới.\n" +
            "- Cảnh nào chỉ còn dưới 4 chữ thì có thể để trống.\n" +
            "Trả về: {\"scenes\":[{\"scene\": số cảnh, \"vi\": \"lời bình mới\", \"en\": \"nghĩa tiếng Anh ngắn gọn\"}]}";
        return (system, user);
    }

    public static Dictionary<int, (string Vi, string En)> ParseCondensed(string json)
    {
        var result = new Dictionary<int, (string, string)>();
        try
        {
            if (JsonNode.Parse(json)?["scenes"] is not JsonArray arr) return result;
            foreach (var x in arr.OfType<JsonObject>())
            {
                if (x["scene"] is not JsonValue sv || !sv.TryGetValue<int>(out var n)) continue;
                result[n] = (x["vi"]?.GetValue<string>() ?? "", x["en"]?.GetValue<string>() ?? "");
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException) { result.Clear(); }
        return result;
    }

    /// <summary>At analysis: a reference over a minute is condensed to one (lengths, then the narration — best effort).</summary>
    private async Task FitNewAnalysisAsync(AiStudioProject p, IReadOnlyList<AiStudioScene> scenes, CancellationToken ct)
    {
        if (!ApplyDurationPlan(scenes)) return;
        await _db.SaveChangesAsync(ct);
        try { await CondenseNarrationAsync(p, scenes, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // the narration can be shortened later from the page ("Rút gọn lời bình"); the analysis itself is fine
            _logger.LogWarning(ex, "AI Studio: narration of {Project} not condensed", p.Id);
        }
    }
}
