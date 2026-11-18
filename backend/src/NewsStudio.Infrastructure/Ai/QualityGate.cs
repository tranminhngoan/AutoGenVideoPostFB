using NewsStudio.Application.DTOs;

namespace NewsStudio.Infrastructure.Ai;

/// <summary>
/// Decides whether a video may be posted automatically:
/// <list type="bullet">
/// <item><b>REJECTED</b> — the AI editor found the headline/text says something the article doesn't.</item>
/// <item><b>REVIEW_REQUIRED</b> — numbers that aren't in the article, claims without a source sentence,
/// unreadable article, picture problems, a story outside the page's topic: a person must look before it goes out
/// (an automatic post takes another story instead).</item>
/// <item><b>APPROVED</b> — passed every automatic check (an internal standard, not Meta's).</item>
/// </list>
/// </summary>
public static class QualityGate
{
    /// <summary>Below this, the article text is too thin to check the script against.</summary>
    public const int MinArticleChars = 300;

    public static QualityGateDto Evaluate(FactSheetDto? sheet, EditorialPassDto? pass, int articleChars, IEnumerable<string>? pictureProblems = null)
    {
        var gate = new QualityGateDto { CheckedAtUtc = DateTime.UtcNow };

        if (pass is { Consistent: false })
        {
            gate.Status = QualityGateStatus.Rejected;
            gate.Reasons.AddRange(pass.Issues.Count > 0 ? pass.Issues : new List<string> { "Tiêu đề/nội dung video chưa khớp bài gốc." });
            return gate;
        }

        if (articleChars < MinArticleChars)
            gate.Reasons.Add("Không đọc được đủ nội dung bài gốc để kiểm chứng kịch bản.");
        if (sheet == null)
        {
            gate.Reasons.Add("Video chưa có hồ sơ kiểm chứng (kịch bản viết trước khi có tính năng này). Viết lại kịch bản để kiểm chứng.");
        }
        else
        {
            if (sheet.NumbersNotInArticle.Count > 0)
                gate.Reasons.Add($"Số liệu trong lời đọc không có trong bài gốc: {string.Join(", ", sheet.NumbersNotInArticle.Take(6))}.");
            var unsourced = sheet.Claims.Where(c => !c.QuoteFound).ToList();
            if (unsourced.Count > 1 || (sheet.Claims.Count > 0 && unsourced.Count * 3 > sheet.Claims.Count))
                gate.Reasons.Add($"{unsourced.Count}/{sheet.Claims.Count} ý chính không tìm thấy câu gốc trong bài: \"{Shorten(unsourced[0].Fact)}\"…");
        }
        if (pass == null)
            gate.Reasons.Add("AI chưa đối chiếu tiêu đề và nội dung với bài gốc.");
        else if (!pass.OnTopic)
            gate.Reasons.Add($"Tin không đúng chủ đề của Page: {pass.OffTopicReason ?? "nội dung chính thuộc lĩnh vực khác."}");
        if (pictureProblems != null) gate.Reasons.AddRange(pictureProblems);

        gate.Status = gate.Reasons.Count == 0 ? QualityGateStatus.Approved : QualityGateStatus.ReviewRequired;
        return gate;
    }

    private static string Shorten(string text) => text.Length <= 80 ? text : text[..79] + "…";
}
