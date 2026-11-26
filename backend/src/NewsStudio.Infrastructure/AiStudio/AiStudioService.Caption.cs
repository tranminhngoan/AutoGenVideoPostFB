using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.FilmFactory;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Infrastructure.AiStudio;

/// <summary>Writes a Facebook caption from text (the narration / scene summaries — never the video itself).</summary>
public interface IAiStudioCaptionWriter
{
    Task<(string Caption, decimal CostUsd, long Tokens, string Model)> WriteAsync(string system, string user, CancellationToken ct);
}

public sealed class GeminiCaptionWriter : IAiStudioCaptionWriter
{
    private readonly FilmGeminiClient _gemini;
    public GeminiCaptionWriter(FilmGeminiClient gemini) => _gemini = gemini;

    public async Task<(string Caption, decimal CostUsd, long Tokens, string Model)> WriteAsync(string system, string user, CancellationToken ct)
    {
        var model = _gemini.TextModel;
        var result = await _gemini.GenerateJsonAsync(system, user, 4096, 0.8, ct, model, lightThinking: true);
        var caption = AiStudioService.ParseCaption(result.Text)
                      ?? throw new InvalidOperationException("Gemini không trả về chú thích hợp lệ — bấm viết lại.");
        return (caption, result.CostUsd, result.Tokens, model);
    }
}

/// <summary>
/// The Facebook caption of the joined video: written by Gemini from the story (what the narration says, or the scene
/// summaries), editable, copied with the AI disclosure line Meta requires for realistic AI video.
/// </summary>
public partial class AiStudioService
{
    public const int MaxCaptionChars = 2200;

    public async Task<AiStudioProjectDto> SetCaptionAsync(Guid id, string? text, CancellationToken ct)
    {
        var p = await _db.AiStudioProjects.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        text = text?.Trim() ?? "";
        if (text.Length > MaxCaptionChars) throw new ArgumentException($"Chú thích quá dài (tối đa {MaxCaptionChars} ký tự).");
        p.Caption = text;
        await _db.SaveChangesAsync(ct);
        return await GetProjectAsync(id, ct);
    }

    public async Task<AiStudioProjectDto> WriteCaptionAsync(Guid id, CancellationToken ct)
    {
        var p = await _db.AiStudioProjects.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        var scenes = await _db.AiStudioScenes.AsNoTracking().Where(s => s.ProjectId == id).ToListAsync(ct);
        var (system, user) = BuildCaptionPrompt(p, scenes);
        await _studio.EnsureMonthlyBudgetAsync(0.01m, ct);
        var (caption, cost, tokens, model) = await _captions.WriteAsync(system, user, ct);
        _db.StudioCostRecords.Add(new StudioCostRecord
        {
            Kind = "Plan", Model = model, Tokens = tokens, CostUsd = cost,
            Note = $"AI Video Studio · {Short(p.Title)} · chú thích Facebook"
        });
        p.Caption = caption.Length > MaxCaptionChars ? caption[..MaxCaptionChars] : caption;
        await _db.SaveChangesAsync(ct);
        return await GetProjectAsync(id, ct);
    }

    public static (string System, string User) BuildCaptionPrompt(AiStudioProject p, IReadOnlyList<AiStudioScene> scenes)
    {
        var topic = AiStudioTopics.Find(p.Topic);
        var story = FullNarration(scenes);
        var summaries = scenes.Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.SummaryVi)).OrderBy(s => s.Order).Select(s => "- " + s.SummaryVi!.Trim()).ToList();
        var tone = p.Topic switch
        {
            AiStudioTopics.VietnamChildhood => "ấm áp, hoài niệm, chân thành như kể chuyện tuổi thơ cho bạn bè — gợi kỷ niệm, không bi lụy",
            AiStudioTopics.AsianBeauty => "nhẹ nhàng, tinh tế, ngọt ngào — khen vẻ đẹp một cách lịch sự, không gợi dục",
            _ => "tự nhiên, cuốn hút, chân thành"
        };
        var system = "Bạn viết chú thích đăng Facebook Reels bằng tiếng Việt cho một video ngắn. Chỉ trả về JSON hợp lệ dạng {\"caption\": \"...\"}.";
        var user =
            $"Chủ đề kênh: {(topic != null ? topic.Label + " — " + topic.Description : "chung")}\n" +
            $"Giọng văn: {tone}.\n" +
            $"Tên dự án: {p.Title}\n" +
            (story != null ? $"Lời bình của video:\n\"{Cap(story, 2500)}\"\n" : "") +
            (summaries.Count > 0 ? "Các cảnh trong video:\n" + string.Join("\n", summaries.Take(20)) + "\n" : "") +
            "\nYêu cầu:\n" +
            "- 1–3 câu ngắn, câu đầu là móc câu gợi tò mò hoặc cảm xúc; có thể thêm 1 câu hỏi mời bình luận.\n" +
            "- Chỉ dựa trên nội dung ở trên, không bịa tên người, địa danh hay sự kiện.\n" +
            "- 1–3 emoji hợp cảm xúc; cuối cùng 3–5 hashtag tiếng Việt không dấu hoặc có dấu, liên quan chủ đề.\n" +
            "- Không giật tít, không viết hoa toàn bộ, không nhắc tới AI (dòng ghi chú AI được thêm riêng).";
        return (system, user);
    }

    public static string? ParseCaption(string json)
    {
        try
        {
            var text = JsonNode.Parse(json)?["caption"]?.GetValue<string>()?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }

    private static string Cap(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
