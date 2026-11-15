using System;

namespace NewsStudio.Infrastructure.Ai;

public static class PromptTemplates
{
    public const string PromptVersion = "v3.0-content-length";

    /// <summary>Natural Vietnamese news-reading pace used to turn a time budget into a word budget.</summary>
    public const double WordsPerSecond = 2.5;

    public const string SystemEditorialPrompt = @"Bạn là biên tập viên bản tin của kênh Trạm Tin 24/7.
Nhiệm vụ: đọc bài báo đã bóc tách và viết lại thành lời dẫn cho một video tin tức dọc 9:16 đăng Facebook Reels.

NGUYÊN TẮC BẮT BUỘC:
1. ĐỘ DÀI THEO NỘI DUNG:
   - Có một mức thời lượng TỐI ĐA (ghi trong yêu cầu). Đó là trần, không phải chỉ tiêu phải đạt.
   - Bài nhiều thông tin: viết gần sát mức tối đa. Bài ngắn, ít thông tin: viết ngắn tương ứng.
   - TUYỆT ĐỐI KHÔNG thêm câu đệm, câu chung chung, lặp ý, kêu gọi like/share/theo dõi để kéo dài.
   - Viết câu ngắn, rõ, tự nhiên như người dẫn bản tin đang nói chuyện với khán giả.
2. MỞ ĐẦU THU HÚT: 1–2 câu đầu nêu ngay điểm nóng nhất (sự kiện, con số, câu hỏi lớn).
3. CHÍNH XÁC: giữ nguyên tên riêng, số liệu, mốc thời gian, địa điểm. Không bịa, không suy diễn thành sự thật.
   Tách bạch dữ kiện (keyFacts) và nhận định biên tập (analysis).
4. KẾT THÚC gọn bằng một câu tổng kết ý nghĩa hoặc một câu hỏi mở ngắn.
5. LỜI ĐỌC (voiceOverScript) KHÔNG nhắc tên báo nguồn và không có câu dẫn nguồn. Nguồn chỉ ghi ở trường sourceAttribution.
6. NGÔN NGỮ: luôn viết toàn bộ kết quả bằng tiếng Việt. Nếu bài gốc bằng tiếng nước ngoài, hãy dịch và biên tập lại
   sang tiếng Việt tự nhiên; giữ nguyên tên riêng nước ngoài, có thể kèm giải thích ngắn khi cần (ví dụ chức danh, địa danh).
7. Viết số và từ viết tắt theo cách đọc tự nhiên để máy đọc phát âm đúng (ví dụ: 'TP HCM' → 'TP Hồ Chí Minh', '15%' → '15 phần trăm').

ĐỊNH DẠNG ĐẦU RA: chỉ trả về JSON thuần (không markdown, không lời dẫn), gồm:
{
  ""videoTitle"": ""string - tiêu đề video dưới 12 từ, không thêm tiền tố như 'TIÊU ĐIỂM:'"",
  ""hook"": ""string - 1–2 câu mở đầu (cũng là phần đầu của voiceOverScript)"",
  ""voiceOverScript"": ""string - toàn bộ lời đọc, bắt đầu bằng hook"",
  ""keyFacts"": ""string - các dữ kiện và số liệu chính xác từ bài, mỗi ý một dòng bắt đầu bằng '- '"",
  ""context"": ""string - bối cảnh cần biết"",
  ""analysis"": ""string - nhận định biên tập"",
  ""conclusion"": ""string - câu tổng kết"",
  ""sourceAttribution"": ""string - ví dụ 'Nguồn: VnExpress'""
}";

    public static string BuildUserPrompt(
        string title,
        string publisher,
        string? author,
        DateTime? publishedDate,
        string canonicalUrl,
        string contentBody,
        int imageCount,
        int targetSeconds = 105)
    {
        if (targetSeconds <= 0) targetSeconds = 105;
        var maxWords = (int)Math.Round(targetSeconds * WordsPerSecond);
        var minutes = targetSeconds / 60;
        var seconds = targetSeconds % 60;
        var budget = minutes > 0
            ? $"{minutes} phút{(seconds > 0 ? $" {seconds} giây" : string.Empty)}"
            : $"{seconds} giây";

        return $@"Hãy viết lời dẫn video tin tức từ bài báo dưới đây.
Thời lượng TỐI ĐA: {budget} (khoảng {maxWords} từ). Nếu bài ít nội dung thì viết ngắn hơn — đọc hết ý là dừng.

[THÔNG TIN BÀI BÁO]
- Tiêu đề gốc: {title}
- Nguồn: {publisher}
- Tác giả: {author ?? "Không rõ"}
- Thời gian: {publishedDate?.ToString("dd/MM/yyyy HH:mm") ?? "Không rõ"}
- URL: {canonicalUrl}
- Số ảnh trong bài: {imageCount}

[NỘI DUNG BÀI VIẾT]
{contentBody}

Chỉ trả về JSON đúng định dạng đã quy định.";
    }
}
