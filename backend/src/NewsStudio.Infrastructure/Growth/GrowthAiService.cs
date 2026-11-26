using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.AiStudio;
using NewsStudio.Infrastructure.FilmFactory;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Infrastructure.Growth;

/// <summary>Text AI used by the module (JSON answers). The default is the app's existing Gemini client.</summary>
public interface IGrowthTextAi
{
    Task<(string Json, decimal CostUsd, long Tokens, string Model)> JsonAsync(string system, string user, int maxTokens, double temperature, CancellationToken ct);
}

public sealed class GeminiGrowthTextAi : IGrowthTextAi
{
    private readonly FilmGeminiClient _gemini;
    public GeminiGrowthTextAi(FilmGeminiClient gemini) => _gemini = gemini;

    public async Task<(string Json, decimal CostUsd, long Tokens, string Model)> JsonAsync(string system, string user, int maxTokens, double temperature, CancellationToken ct)
    {
        var model = _gemini.TextModel;
        var r = await _gemini.GenerateJsonAsync(system, user, maxTokens, temperature, ct, model, lightThinking: true);
        return (r.Text, r.CostUsd, r.Tokens, model);
    }
}

/// <summary>
/// Writing help for the campaign: captions in several styles, turning one video into photo posts / Stories / captions /
/// titles, and suggestions from the campaign's real numbers. Only text is generated (no AI images). Every call is
/// checked against the campaign's AI budget and the studio's monthly budget, and recorded in the studio cost log.
/// </summary>
public class GrowthAiService
{
    /// <summary>Upper estimate of one call, used for the budget check before it is made.</summary>
    public const decimal EstimatedCallUsd = 0.01m;

    public static readonly Dictionary<string, string> Styles = new()
    {
        ["Emotional"] = "Cảm xúc — chạm tới cảm xúc thật, chân thành, không sến",
        ["Nostalgic"] = "Gợi ký ức — gợi kỷ niệm, hoài niệm nhẹ nhàng",
        ["Story"] = "Kể chuyện — mở đầu bằng một khoảnh khắc, dẫn dắt ngắn",
        ["Curiosity"] = "Gợi tò mò — câu đầu khiến người đọc muốn xem tiếp, KHÔNG giật tít sai sự thật",
        ["Discussion"] = "Thảo luận — kết bằng câu hỏi mở mời mọi người bình luận",
        ["Knowledge"] = "Chia sẻ kiến thức — một điều thú vị / hữu ích, chỉ khi nội dung nguồn có",
        ["Short"] = "Ngắn gọn — 1 câu, dễ đọc"
    };

    private readonly IApplicationDbContext _db;
    private readonly IGrowthTextAi _ai;
    private readonly StudioService _studio;
    private readonly GrowthCampaignService _campaigns;
    private readonly GrowthInsightsService _insights;

    public GrowthAiService(IApplicationDbContext db, IGrowthTextAi ai, StudioService studio, GrowthCampaignService campaigns, GrowthInsightsService insights)
    {
        _db = db;
        _ai = ai;
        _studio = studio;
        _campaigns = campaigns;
        _insights = insights;
    }

    // ───────────────────────── budget & recording ─────────────────────────

    public static void EnsureCampaignBudget(GrowthCampaign c, decimal estimate)
    {
        if (c.AiBudgetUsd <= 0) throw new InvalidOperationException("Ngân sách AI của chiến dịch là 0 — tăng ở Cài đặt chiến dịch để dùng AI.");
        if (c.AiSpentUsd + estimate > c.AiBudgetUsd)
            throw new InvalidOperationException($"Hết ngân sách AI của chiến dịch: đã dùng {c.AiSpentUsd:0.000} / {c.AiBudgetUsd:0.00} USD. Tăng ngân sách ở Cài đặt chiến dịch nếu muốn tiếp tục.");
    }

    private async Task<string> CallAsync(GrowthCampaign c, string what, string system, string user, int maxTokens, double temperature, CancellationToken ct)
    {
        EnsureCampaignBudget(c, EstimatedCallUsd);
        await _studio.EnsureMonthlyBudgetAsync(EstimatedCallUsd, ct);
        var (json, cost, tokens, model) = await _ai.JsonAsync(system, user, maxTokens, temperature, ct);
        c.AiSpentUsd += cost;
        _db.StudioCostRecords.Add(new StudioCostRecord { Kind = "Plan", Model = model, Tokens = tokens, CostUsd = cost, Note = $"Chiến dịch tăng trưởng · {Cap(c.Name, 60)} · {what}" });
        _db.GrowthActivityLogs.Add(new GrowthActivityLog { CampaignId = c.Id, Action = "AiCall", Message = $"AI: {what} ({cost:0.0000} USD).", Actor = "user" });
        await _db.SaveChangesAsync(ct);
        LastCost = cost;
        return json;
    }

    private decimal LastCost { get; set; }

    // ───────────────────────── source text ─────────────────────────

    /// <summary>What a video is about, from the existing tools' own text (title, script, narration, caption) — never the video itself.</summary>
    public async Task<(string Title, string Text)> SourceTextAsync(GrowthCampaign c, string type, Guid id, CancellationToken ct)
    {
        switch (type)
        {
            case GrowthSources.StudioVideo:
            {
                var v = await _db.StudioVideos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video Studio.");
                if (v.FacebookAccountId != null && v.FacebookAccountId != c.FacebookAccountId) throw new ArgumentException("Video này thuộc Page khác.");
                return (v.Title, Join(("Tiêu đề", v.Title), ("Kịch bản", Cap(v.Script, 3000)), ("Chú thích cũ", v.Caption)));
            }
            case GrowthSources.AiStudioProject:
            {
                var p = await _db.AiStudioProjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy dự án AI Video Studio.");
                if (p.FacebookAccountId != null && p.FacebookAccountId != c.FacebookAccountId) throw new ArgumentException("Video này thuộc Page khác.");
                var scenes = await _db.AiStudioScenes.AsNoTracking().Where(s => s.ProjectId == id && s.Enabled).OrderBy(s => s.Order).ToListAsync(ct);
                var summaries = string.Join("\n", scenes.Where(s => !string.IsNullOrWhiteSpace(s.SummaryVi)).Take(20).Select(s => "- " + s.SummaryVi!.Trim()));
                return (p.Title, Join(("Tiêu đề", p.Title), ("Lời bình", Cap(AiStudioService.FullNarration(scenes) ?? "", 2500)), ("Các cảnh", summaries), ("Chú thích cũ", p.Caption)));
            }
            case GrowthSources.VideoProject:
            {
                var n = await _db.VideoProjects.AsNoTracking().Include(x => x.Script).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video tin tức.");
                if (n.FacebookAccountId != null && n.FacebookAccountId != c.FacebookAccountId) throw new ArgumentException("Video này thuộc Page khác.");
                return (n.Title, Join(("Tiêu đề", n.Title), ("Lời dẫn", Cap(n.Script?.VoiceOverScript ?? "", 2500)), ("Dữ kiện", Cap(n.Script?.KeyFacts ?? "", 1000))));
            }
            default:
                throw new ArgumentException("Nguồn video không hợp lệ.");
        }
    }

    private static string Join(params (string Label, string? Text)[] parts) =>
        string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p.Text)).Select(p => $"{p.Label}: {p.Text!.Trim()}"));

    // ───────────────────────── captions ─────────────────────────

    public const string Rules =
        "- Chỉ dựa trên nội dung được cung cấp: KHÔNG bịa tên người, địa danh, số liệu, sự kiện.\n" +
        "- KHÔNG giật tít sai sự thật, không viết hoa toàn bộ, không hứa hẹn quá mức, không kêu gọi kiểu \"comment 1 nếu…\".\n" +
        "- Tối đa 5 hashtag mỗi phiên bản, liên quan nội dung, không spam.\n" +
        "- Các phiên bản phải khác nhau rõ rệt về câu mở đầu và cách viết, không lặp ý.\n" +
        "- Tiếng Việt tự nhiên, gần gũi; 0–3 emoji phù hợp.";

    public static (string System, string User) BuildCaptionPrompt(string pageName, string brief, IReadOnlyList<string> styles, int variants, string kind)
    {
        var system = "Bạn là biên tập viên mạng xã hội cho một Facebook Page Việt Nam. Viết caption trung thực, cuốn hút. Chỉ trả về JSON hợp lệ.";
        var styleLines = string.Join("\n", styles.Select(s => $"- {s}: {Styles[s]}"));
        var user =
            $"Page: {pageName}\nLoại bài: {kind}\n\nNội dung:\n{Cap(brief, 4000)}\n\n" +
            $"Viết {variants} phiên bản caption, lần lượt theo các phong cách:\n{styleLines}\n\n" +
            $"Quy tắc:\n{Rules}\n\n" +
            "JSON: {\"variants\":[{\"style\":\"<mã phong cách>\",\"caption\":\"...\",\"hashtags\":[\"#...\"],\"question\":\"câu hỏi gợi bình luận (tuỳ chọn)\"}]}";
        return (system, user);
    }

    public static List<GrowthCaptionVariantDto> ParseVariants(string json)
    {
        var list = new List<GrowthCaptionVariantDto>();
        try
        {
            var arr = JsonNode.Parse(json)?["variants"]?.AsArray();
            if (arr == null) return list;
            foreach (var v in arr)
            {
                var caption = v?["caption"]?.GetValue<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(caption)) continue;
                list.Add(new GrowthCaptionVariantDto
                {
                    Style = v?["style"]?.GetValue<string>() ?? "",
                    Caption = caption.Length > GrowthContentService.MaxCaption ? caption[..GrowthContentService.MaxCaption] : caption,
                    Hashtags = Tags(v?["hashtags"]),
                    Question = v?["question"]?.GetValue<string>()?.Trim()
                });
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return list;
    }

    private static List<string> Tags(JsonNode? node)
    {
        if (node is not JsonArray arr) return new();
        return arr.Select(t => t?.GetValue<string>()?.Trim() ?? "").Where(t => t.Length > 1)
            .Select(t => t.StartsWith('#') ? t : "#" + t).Select(t => t.Replace(" ", ""))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
    }

    public async Task<GrowthCaptionResultDto> CaptionsAsync(Guid campaignId, GrowthCaptionRequestDto input, CancellationToken ct)
    {
        var c = await _campaigns.LoadAsync(campaignId, ct);
        var brief = (input.Brief ?? "").Trim();
        if (input.SourceType is { Length: > 0 } st && input.SourceId is { } sid)
        {
            var (_, text) = await SourceTextAsync(c, st, sid, ct);
            brief = brief.Length > 0 ? brief + "\n" + text : text;
        }
        if (brief.Length < 10) throw new ArgumentException("Hãy mô tả nội dung bài (ít nhất vài từ) hoặc chọn video nguồn.");
        var styles = (input.Styles ?? new()).Where(Styles.ContainsKey).Distinct().ToList();
        if (styles.Count == 0) styles = new() { "Emotional", "Curiosity", "Discussion" };
        var variants = Math.Clamp(input.Variants ?? styles.Count, 1, 7);
        while (styles.Count < variants) styles.Add(styles[styles.Count % Math.Max(1, styles.Count)]);
        var (system, user) = BuildCaptionPrompt(c.PageName, brief, styles.Take(variants).ToList(), variants, input.Kind ?? "bài đăng");
        var json = await CallAsync(c, "viết caption", system, user, 3000, 0.9, ct);
        var parsed = ParseVariants(json);
        if (parsed.Count == 0) throw new InvalidOperationException("AI không trả về caption hợp lệ — thử lại.");
        return new GrowthCaptionResultDto { Variants = parsed, CostUsd = LastCost, CampaignAiSpentUsd = c.AiSpentUsd };
    }

    // ───────────────────────── repurpose ─────────────────────────

    public static (string System, string User) BuildRepurposePrompt(string pageName, string source)
    {
        var system = "Bạn là biên tập viên mạng xã hội. Từ một video đã có, đề xuất nội dung phụ để đăng thêm — chỉ chữ, không tạo ảnh. Chỉ trả về JSON hợp lệ.";
        var user =
            $"Page: {pageName}\n\nVideo nguồn:\n{Cap(source, 5000)}\n\n" +
            "Tạo:\n- 2 bài đăng ảnh (tiêu đề ngắn để in lên ảnh ≤ 8 chữ, 1–2 câu nội dung in lên ảnh, caption bài)\n" +
            "- 3 nội dung Story (mỗi cái 1 câu ngắn ≤ 15 chữ để in lên ảnh dọc, kèm gợi ý sticker/câu hỏi nếu hợp)\n" +
            "- 3 caption khác nhau (Cảm xúc, Gợi tò mò, Thảo luận)\n- 5 tiêu đề thay thế\n- 1 bộ hashtag (≤ 6)\n" +
            "- 1 câu hỏi khuyến khích bình luận (gợi người xem kể trải nghiệm thật, không câu kéo)\n" +
            "- 1 lời mời theo dõi Page tự nhiên, không nài nỉ\n\n" +
            $"Quy tắc:\n{Rules}\n\n" +
            "JSON: {\"photoPosts\":[{\"headline\":\"\",\"body\":\"\",\"caption\":\"\"}],\"stories\":[{\"text\":\"\",\"sticker\":\"\"}]," +
            "\"captions\":[{\"style\":\"Emotional\",\"caption\":\"\",\"hashtags\":[]}],\"titles\":[\"\"],\"hashtags\":[\"#\"],\"commentQuestion\":\"\",\"followCta\":\"\"}";
        return (system, user);
    }

    public static GrowthRepurposeResultDto ParseRepurpose(string json)
    {
        var r = new GrowthRepurposeResultDto();
        try
        {
            var root = JsonNode.Parse(json);
            if (root == null) return r;
            string S(JsonNode? n) => n?.GetValue<string>()?.Trim() ?? "";
            if (root["photoPosts"] is JsonArray pp)
                r.PhotoPosts = pp.Select(p => new GrowthPhotoIdeaDto { Headline = S(p?["headline"]), Body = S(p?["body"]), Caption = S(p?["caption"]) })
                    .Where(p => p.Headline.Length > 0 || p.Body.Length > 0).Take(2).ToList();
            if (root["stories"] is JsonArray st)
                r.Stories = st.Select(s => new GrowthStoryIdeaDto { Text = S(s?["text"]), Sticker = S(s?["sticker"]) is { Length: > 0 } k ? k : null })
                    .Where(s => s.Text.Length > 0).Take(3).ToList();
            r.Captions = ParseVariants(new JsonObject { ["variants"] = root["captions"]?.DeepClone() }.ToJsonString()).Take(3).ToList();
            if (root["titles"] is JsonArray ti) r.Titles = ti.Select(S).Where(t => t.Length > 0).Distinct().Take(5).ToList();
            r.Hashtags = Tags(root["hashtags"]);
            r.CommentQuestion = S(root["commentQuestion"]);
            r.FollowCta = S(root["followCta"]);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return r;
    }

    public async Task<GrowthRepurposeResultDto> RepurposeAsync(Guid campaignId, GrowthRepurposeRequestDto input, CancellationToken ct)
    {
        var c = await _campaigns.LoadAsync(campaignId, ct);
        var (title, text) = await SourceTextAsync(c, input.SourceType, input.SourceId, ct);
        if (text.Length < 10) throw new InvalidOperationException("Video nguồn chưa có tiêu đề / lời bình để AI dựa vào.");
        var (system, user) = BuildRepurposePrompt(c.PageName, text);
        var json = await CallAsync(c, $"biến thể nội dung từ \"{Cap(title, 40)}\"", system, user, 4000, 0.85, ct);
        var r = ParseRepurpose(json);
        if (r.PhotoPosts.Count == 0 && r.Captions.Count == 0 && r.Titles.Count == 0) throw new InvalidOperationException("AI không trả về nội dung hợp lệ — thử lại.");
        r.SourceTitle = title;
        r.CostUsd = LastCost;
        return r;
    }

    // ───────────────────────── suggestions ─────────────────────────

    /// <summary>Suggestions for the next day from the campaign's real numbers; plain rules when there is not enough data (no AI call then).</summary>
    public async Task<GrowthSuggestionsDto> SuggestionsAsync(Guid campaignId, bool useAi, CancellationToken ct)
    {
        var c = await _campaigns.LoadAsync(campaignId, ct);
        var o = await _insights.OverviewAsync(campaignId, ct);
        var posts = await _insights.PostsAsync(campaignId, ct);
        var hours = await _insights.HoursAsync(campaignId, ct);
        var withNumbers = posts.Where(p => p.Views != null || p.Engagement != null).ToList();
        if (!useAi || withNumbers.Count < 3)
        {
            var basic = new List<string>(o.Insights);
            if (withNumbers.Count < 3) basic.Add($"Cần ít nhất 3 bài có số liệu để AI phân tích (hiện có {withNumbers.Count}). Tiếp tục đăng theo kế hoạch và đồng bộ số liệu.");
            if (hours.EnoughData && hours.SuggestedSlots.Count > 0) basic.Add($"Khung giờ có lượt xem trung bình cao nhất ({hours.Basis}): {string.Join(", ", hours.SuggestedSlots)}.");
            return new GrowthSuggestionsDto { Suggestions = basic, Basis = $"{withNumbers.Count} bài có số liệu", FromAi = false };
        }
        var sb = new StringBuilder();
        sb.AppendLine($"Chiến dịch: {c.Name} — Page {c.PageName}, ngày {o.Progress.Day}/{o.Progress.TotalDays}.");
        foreach (var k in o.Kpis.Where(k => k.Value != null)) sb.AppendLine($"- {k.Label}: {k.Value}{k.Unit}");
        sb.AppendLine("Các bài (loại | giờ đăng | phong cách | CTA | độ dài caption | hashtag | views | tương tác | chia sẻ | follow):");
        foreach (var p in withNumbers.Take(40))
            sb.AppendLine($"- {p.Kind} | {p.LocalDate} {p.LocalTime} | {p.Style ?? "-"} | {Cap(p.Cta ?? "-", 60)} | {p.CaptionLength} | {p.HashtagCount} | {p.Views?.ToString() ?? "?"} | {p.Engagement?.ToString() ?? "?"} | {p.Shares?.ToString() ?? "?"} | {p.Follows?.ToString() ?? "?"} | \"{Cap(p.Title.Length > 0 ? p.Title : p.CaptionExcerpt, 80)}\"");
        if (hours.Buckets.Count > 0) sb.AppendLine("Giờ đăng (giờ: số bài, view TB): " + string.Join("; ", hours.Buckets.Select(b => $"{b.Label}: {b.Posts}, {b.AvgViews}")));
        var system = "Bạn là chuyên gia tăng trưởng Facebook Page. Phân tích dữ liệu THẬT được cung cấp, không suy diễn quá dữ liệu. Chỉ trả về JSON hợp lệ.";
        var user = sb + "\nĐưa 3–6 đề xuất cụ thể cho ngày tiếp theo (chủ đề, loại nội dung, caption/CTA, giờ đăng). " +
                   "Nói rõ khi dữ liệu còn ít. Không đề xuất mua tương tác, spam bình luận, follow chéo hay bất kỳ hành vi giả lập nào. " +
                   "Không tự thay đổi lịch đã duyệt — chỉ đề xuất.\nJSON: {\"suggestions\":[\"...\"]}";
        var json = await CallAsync(c, "gợi ý từ số liệu", system, user, 2000, 0.5, ct);
        var list = new List<string>();
        try
        {
            if (JsonNode.Parse(json)?["suggestions"] is JsonArray arr)
                list = arr.Select(x => x?.GetValue<string>()?.Trim() ?? "").Where(x => x.Length > 0).Take(8).ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        if (list.Count == 0) throw new InvalidOperationException("AI không trả về gợi ý hợp lệ — thử lại.");
        return new GrowthSuggestionsDto { Suggestions = list, Basis = $"{withNumbers.Count} bài có số liệu", FromAi = true, CostUsd = LastCost };
    }

    /// <summary>A short AI comment on the report (used by the report service when asked).</summary>
    public async Task<string?> ReportCommentaryAsync(GrowthCampaign c, string facts, CancellationToken ct)
    {
        var system = "Bạn là chuyên gia tăng trưởng Facebook Page, viết nhận xét ngắn cho chủ Page. Trung thực, chỉ dựa trên số liệu. Chỉ trả về JSON hợp lệ.";
        var user = facts + "\n\nViết nhận xét 4–8 câu: kết quả chính, điều hiệu quả / chưa hiệu quả, có nên tiếp tục Meta One không. " +
                   "KHÔNG khẳng định Meta One trực tiếp tạo ra toàn bộ follower tăng thêm khi chưa có bằng chứng (follower tăng có thể do nội dung, thời điểm, xu hướng). " +
                   "JSON: {\"commentary\":\"...\"}";
        var json = await CallAsync(c, "nhận xét báo cáo", system, user, 2000, 0.4, ct);
        try { return JsonNode.Parse(json)?["commentary"]?.GetValue<string>()?.Trim(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>AI reading of the owner's competitor notes (topics, formats, what worked).</summary>
    public async Task<(List<string> Notes, decimal Cost)> CompetitorNotesAsync(GrowthCampaign c, string observations, CancellationToken ct)
    {
        var system = "Bạn phân tích ghi chép về các Page đối thủ do chủ Page tự nhập (dữ liệu công khai). Chỉ trả về JSON hợp lệ.";
        var user = $"Ghi chép:\n{Cap(observations, 6000)}\n\nRút ra 3–6 nhận định: chủ đề phổ biến, kiểu nội dung, cách đặt tiêu đề, tần suất, nội dung tương tác tốt, " +
                   "và gợi ý học hỏi có đạo đức (không sao chép nội dung, không đăng lại video của người khác). Chỉ dựa trên ghi chép.\nJSON: {\"notes\":[\"...\"]}";
        var json = await CallAsync(c, "phân tích đối thủ", system, user, 2000, 0.5, ct);
        try
        {
            var notes = JsonNode.Parse(json)?["notes"] is JsonArray arr ? arr.Select(x => x?.GetValue<string>()?.Trim() ?? "").Where(x => x.Length > 0).Take(8).ToList() : new();
            return (notes, LastCost);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return (new(), LastCost); }
    }

    private static string Cap(string? s, int max) => s == null ? "" : s.Length <= max ? s : s[..max] + "…";
}
