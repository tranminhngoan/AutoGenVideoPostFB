using System.Text.RegularExpressions;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Infrastructure.Extractors;

public class ImageClassifierService : IImageClassifierService
{
    public ImageCategory ClassifyImage(string? caption, string? altText, string articleContext)
    {
        var text = $"{caption} {altText}".ToLowerInvariant();

        // 1. Unsuitable checks (ad, logo, social button)
        if (text.Contains("quảng cáo") || text.Contains("banner") || text.Contains("logo") || text.Contains("icon") || text.Contains(" qc"))
        {
            return ImageCategory.Unsuitable;
        }

        // 2. Chart checks
        if (text.Contains("biểu đồ") || text.Contains("đồ thị") || text.Contains("thống kê") || text.Contains("sơ đồ") || text.Contains("infographic"))
        {
            return ImageCategory.Chart;
        }

        // 3. Document checks
        if (text.Contains("tài liệu") || text.Contains("văn bản") || text.Contains("quyết định") || text.Contains("nghị định") || text.Contains("hợp đồng") || text.Contains("chứng nhận"))
        {
            return ImageCategory.Document;
        }

        // 4. Panorama / Aerial / Scenic checks
        if (text.Contains("toàn cảnh") || text.Contains("phong cảnh") || text.Contains("quang cảnh") || text.Contains("từ trên cao") || text.Contains("nhìn từ") || text.Contains("flycam") || text.Contains("không gian"))
        {
            return ImageCategory.Panorama;
        }

        // 5. Character / People checks (Without speculative identity assumptions, matching whole tokens)
        if (Regex.IsMatch(text, @"(?:\b|\s|^)(ông|bà|chủ tịch|thủ tướng|bộ trưởng|giáo sư|tiến sĩ|bác sĩ|người dân|chuyên gia|đại biểu|nhân viên|công nhân)(?:\b|\s|$)"))
        {
            return ImageCategory.Character;
        }

        // 6. Event checks
        if (text.Contains("lễ") || text.Contains("hội nghị") || text.Contains("khởi công") || text.Contains("khánh thành") || text.Contains("họp báo") || text.Contains("diễn đàn") || text.Contains("sự kiện") || text.Contains("thi đấu"))
        {
            return ImageCategory.Event;
        }

        // 7. Default to Panorama / Scenic
        return ImageCategory.Panorama;
    }
}
