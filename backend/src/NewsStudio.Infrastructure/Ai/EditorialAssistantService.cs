using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Infrastructure.Trending;

namespace NewsStudio.Infrastructure.Ai;

/// <summary>
/// One Gemini call before a video is posted:
/// <list type="bullet">
/// <item>checks that the video's headline, hook and narration say only what the original article says
/// (no wrong numbers / names, no misleading exaggeration) — auto-posting is blocked when they don't;</item>
/// <item>writes a professional caption (hook line, key facts, source, topic hashtags, no engagement bait);</item>
/// <item>writes the page's first comment: an open question about the story, plus the article link.</item>
/// </list>
/// </summary>
public class EditorialAssistantService : IEditorialAssistantService
{
    private const int ArticleChars = 5000;

    private readonly IApplicationDbContext _context;
    private readonly GeminiAiEditorService _gemini;
    private readonly ILogger<EditorialAssistantService> _logger;

    public EditorialAssistantService(IApplicationDbContext context, GeminiAiEditorService gemini, ILogger<EditorialAssistantService> logger)
    {
        _context = context;
        _gemini = gemini;
        _logger = logger;
    }

    private const string SystemPrompt =
        "Bạn là biên tập viên trưởng của một trang tin trên Facebook. Trước khi đăng một video tin tức, bạn làm 3 việc và chỉ trả về JSON.\n" +
        "1) KIỂM TRA: so tiêu đề, câu mở đầu, chữ trên video và lời đọc với BÀI GỐC. Đặt \"consistent\": false CHỈ khi có lỗi nghiêm trọng: " +
        "sai con số, sai tên người/tổ chức/địa điểm, khẳng định điều bài gốc không nói, hoặc giật tít làm sai bản chất sự việc. " +
        "Khác cách diễn đạt, rút gọn, hay thiếu chi tiết phụ thì vẫn là consistent: true. Liệt kê từng lỗi trong \"issues\" (tiếng Việt, ngắn).\n" +
        "2) CAPTION: tiếng Việt, chuyên nghiệp. Dòng 1: câu mở đầu gây tò mò nhưng đúng sự thật (tối đa 15 chữ). Một dòng trống. " +
        "Sau đó 2–3 dòng ý chính, mỗi dòng bắt đầu bằng \"▪️ \", có con số/tên cụ thể từ bài gốc. Không kêu gọi like/share/tag/bình luận, " +
        "không viết hoa toàn bộ, không quá 2 emoji. Không ghi nguồn và hashtag trong caption (hệ thống tự thêm).\n" +
        "3) HASHTAGS: 3–4 hashtag tiếng Việt không dấu, viết liền, đúng chủ đề tin (ví dụ #TinNong #KinhTe).\n" +
        "4) FIRST_COMMENT: một câu hỏi mở về chính sự việc để người đọc nêu ý kiến (tối đa 25 chữ), trung lập, " +
        "không phải dạng \"bình luận 1 nếu…\", không link.";

    public async Task<EditorialPassDto> RunAsync(Guid videoProjectId, CancellationToken cancellationToken = default)
    {
        var project = await _context.VideoProjects
            .Include(p => p.Script)
            .Include(p => p.Article)
            .Include(p => p.Scenes)
            .FirstOrDefaultAsync(p => p.Id == videoProjectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy video.");
        if (project.Script == null) throw new InvalidOperationException("Video chưa có kịch bản.");

        var article = project.Article;
        var articleText = Regex.Replace(article?.ContentBody ?? article?.Description ?? string.Empty, @"\s+", " ").Trim();
        if (articleText.Length > ArticleChars) articleText = articleText[..ArticleChars];

        var sb = new StringBuilder();
        sb.AppendLine($"Chủ đề trang: {NewsTopics.NameOf(project.Topic ?? NewsTopics.General)}");
        sb.AppendLine("=== VIDEO ===");
        sb.AppendLine($"Tiêu đề: {project.Title}");
        sb.AppendLine($"Câu mở đầu: {project.Script.Hook}");
        var onScreen = project.Scenes.OrderBy(s => s.SceneNumber).Select(s => s.OnScreenText).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (onScreen.Count > 0) sb.AppendLine($"Chữ trên video: {string.Join(" | ", onScreen)}");
        sb.AppendLine($"Lời đọc: {project.Script.VoiceOverScript}");
        sb.AppendLine("=== BÀI GỐC ===");
        sb.AppendLine($"Tiêu đề: {article?.Title ?? "(không có)"}");
        sb.AppendLine($"Nguồn: {project.Publisher}");
        sb.AppendLine($"Nội dung: {(articleText.Length > 0 ? articleText : "(không đọc được nội dung bài gốc)")}");
        sb.AppendLine();
        sb.AppendLine("Trả về đúng định dạng: {\"consistent\": true, \"issues\": [], \"caption\": \"...\", \"hashtags\": [\"#...\"], \"first_comment\": \"...\"}");

        var json = await _gemini.GenerateJsonAsync(SystemPrompt, sb.ToString(), cancellationToken);
        var pass = Parse(json, project.Script.SourceAttribution, project.SourceUrl, hasArticle: articleText.Length > 0);

        project.EditorialPassJson = JsonSerializer.Serialize(pass);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Editorial pass for {ProjectId}: consistent={Consistent}, {Issues} issue(s)", videoProjectId, pass.Consistent, pass.Issues.Count);
        return pass;
    }

    /// <summary>Builds the final caption and first comment from the AI's JSON.</summary>
    public static EditorialPassDto Parse(string json, string? sourceAttribution, string sourceUrl, bool hasArticle = true)
    {
        var text = json.Trim();
        if (text.StartsWith("```"))
        {
            text = Regex.Replace(text, @"^```[a-zA-Z]*\s*", "");
            text = Regex.Replace(text, @"\s*```$", "");
        }
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        string Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : string.Empty;
        List<string> List(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.Trim()).Where(s => s.Length > 0).ToList()
            : new List<string>();

        var consistent = !(root.TryGetProperty("consistent", out var c) && c.ValueKind == JsonValueKind.False);
        var issues = List("issues");
        if (!hasArticle) consistent = true; // nothing to compare against: don't block on a guess

        var caption = Str("caption");
        if (caption.Length == 0) throw new InvalidOperationException("AI không viết được caption.");
        var hashtags = List("hashtags")
            .Select(h => "#" + Regex.Replace(h.TrimStart('#'), @"[^\p{L}\p{Nd}_]", ""))
            .Where(h => h.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        var parts = new List<string> { caption };
        if (!string.IsNullOrWhiteSpace(sourceAttribution)) parts.Add(sourceAttribution.Trim());
        if (hashtags.Count > 0) parts.Add(string.Join(" ", hashtags));

        var question = Str("first_comment");
        var comment = string.IsNullOrWhiteSpace(sourceUrl)
            ? question
            : (question.Length > 0 ? question + "\n\n" : "") + $"📰 Đọc chi tiết: {sourceUrl}";

        return new EditorialPassDto
        {
            Consistent = consistent,
            Issues = consistent ? new List<string>() : issues,
            Caption = string.Join("\n\n", parts),
            FirstComment = comment,
            CheckedAtUtc = DateTime.UtcNow
        };
    }
}
