using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NewsStudio.Infrastructure.Trending;

/// <param name="Keywords">
/// Lower-case, accent-free phrases matched on whole words in the title + summary of stories from
/// general feeds (home pages). A phrase starting with "=" is matched case-sensitively on the original
/// text (e.g. "=AI", so the Vietnamese word "ai" = "who" doesn't count).
/// </param>
public record NewsTopic(string Key, string Name, string Description, string[] Keywords);

/// <summary>
/// News topics a Facebook Page can be dedicated to. A story gets a topic when it comes from that
/// section's RSS feed (e.g. VnExpress "Thể thao"), or when a general-feed story's title/summary matches
/// the topic's keywords.
/// </summary>
public static class NewsTopics
{
    /// <summary>Every topic (the original behaviour).</summary>
    public const string General = "tong-hop";
    /// <summary>Only the page's own keywords decide.</summary>
    public const string Custom = "tu-chon";

    public static readonly IReadOnlyList<NewsTopic> All = new List<NewsTopic>
    {
        new(General, "Tổng hợp", "Mọi tin hot nhất, không giới hạn chủ đề.", Array.Empty<string>()),
        new("thoi-su", "Thời sự", "Chính sách, xã hội, thiên tai, giao thông trong nước.", new[]
        {
            "chinh phu", "quoc hoi", "thu tuong", "bo truong", "chu tich nuoc", "tong bi thu", "nghi dinh", "nghi quyet",
            "bao so", "con bao", "mua lu", "ngap lut", "lu lut", "sat lo", "dong dat", "un tac", "cao toc", "tau dien",
            "metro", "san bay long thanh", "ubnd", "dai bieu", "chinh sach"
        }),
        new("the-gioi", "Thế giới", "Tin quốc tế: chính trị, xung đột, ngoại giao.", new[]
        {
            "tong thong my", "nuoc my", "ong trump", "ong putin", "trung quoc", "nuoc nga", "tong thong nga", "moscow", "ukraine",
            "israel", "gaza", "hamas", "iran", "trieu tien", "han quoc", "nhat ban", "lien hop quoc", "nato", "chau au",
            "lien minh chau au", "eu", "dai loan", "an do", "philippines", "thai lan", "campuchia", "trump", "putin",
            "white house", "kremlin", "united nations"
        }),
        new("kinh-doanh", "Kinh doanh", "Kinh tế, tài chính, chứng khoán, giá vàng, doanh nghiệp.", new[]
        {
            "chung khoan", "co phieu", "vn index", "vnindex", "ngan hang", "lai suat", "ty gia", "gia vang", "vang mieng",
            "doanh nghiep", "xuat khau", "nhap khau", "lam phat", "gdp", "gia xang", "tien dien", "trai phieu",
            "loi nhuan", "doanh thu", "thue suat", "thue quan", "thue thu nhap", "stocks", "shares", "stock market", "economy", "inflation", "interest rates",
            "earnings", "tariff", "tariffs", "wall street", "federal reserve", "bitcoin", "crypto"
        }),
        new("bat-dong-san", "Bất động sản", "Nhà đất, căn hộ, dự án, giá nhà.", new[]
        {
            "bat dong san", "nha dat", "can ho", "chung cu", "nha o xa hoi", "gia nha", "dat nen", "so do", "du an nha o",
            "real estate", "housing market", "mortgage", "home prices"
        }),
        new("cong-nghe", "Công nghệ", "Điện thoại, AI, mạng xã hội, an ninh mạng, xe điện công nghệ.", new[]
        {
            "=AI", "cong nghe", "tri tue nhan tao", "dien thoai", "smartphone", "iphone", "samsung", "apple", "google",
            "microsoft", "meta", "facebook", "tiktok", "youtube", "chatgpt", "openai", "gemini", "chip", "ban dan",
            "nvidia", "ung dung", "phan mem", "hacker", "tin tac", "an ninh mang", "du lieu ca nhan", "5g", "starlink",
            "robot", "technology", "software", "startup", "cyber", "artificial intelligence"
        }),
        new("the-thao", "Thể thao", "Bóng đá, tennis, đội tuyển, các giải đấu.", new[]
        {
            "bong da", "doi tuyen", "tuyen viet nam", "v league", "sea games", "asiad", "world cup", "euro", "ngoai hang anh",
            "premier league", "champions league", "la liga", "hlv", "cau thu", "ban thang", "tennis", "quan vot",
            "olympic", "vo dich", "football", "soccer", "nba", "f1", "formula 1", "golf", "boxing", "man utd",
            "man city", "arsenal", "liverpool", "chelsea", "real madrid", "barcelona", "messi", "ronaldo"
        }),
        new("giai-tri", "Giải trí", "Sao Việt, phim ảnh, âm nhạc, hoa hậu, showbiz.", new[]
        {
            "ca si", "dien vien", "hoa hau", "a hau", "phim", "showbiz", "nghe si", "mv", "concert", "liveshow",
            "am nhac", "sao viet", "hollywood", "kpop", "k pop", "oscar", "grammy", "netflix", "album", "rapper",
            "movie", "film", "singer", "actor", "actress", "celebrity", "box office"
        }),
        new("suc-khoe", "Sức khoẻ", "Bệnh, thuốc, dinh dưỡng, cảnh báo y tế.", new[]
        {
            "benh vien", "bac si", "ung thu", "benh nhan", "vac xin", "vaccine", "suc khoe", "ngo doc", "dich benh",
            "sot xuat huyet", "cum a", "cum mua", "covid", "dinh duong", "dot quy", "tieu duong", "huyet ap", "bo y te",
            "health", "cancer", "hospital", "disease", "virus", "medical", "doctors", "outbreak"
        }),
        new("giao-duc", "Giáo dục", "Thi cử, tuyển sinh, học đường, du học.", new[]
        {
            "hoc sinh", "sinh vien", "dai hoc", "truong hoc", "thi tot nghiep", "tuyen sinh", "giao vien", "bo gd",
            "hoc phi", "diem chuan", "diem thi", "du hoc", "hoc bong", "school", "university", "students", "teachers",
            "education"
        }),
        new("phap-luat", "Pháp luật", "Vụ án, điều tra, xét xử, lừa đảo, an ninh trật tự.", new[]
        {
            "khoi to", "bat giu", "tam giam", "xet xu", "toa an", "toa tuyen", "lua dao", "vu an", "cong an", "truy na",
            "ma tuy", "giet nguoi", "trom cap", "cuop", "hinh su", "vien kiem sat", "dieu tra", "police", "court",
            "arrested", "trial", "murder", "lawsuit", "charged", "sentenced", "prosecutors"
        }),
        new("doi-song", "Đời sống", "Gia đình, hôn nhân, ẩm thực, chuyện đời thường.", new[]
        {
            "gia dinh", "vo chong", "con cai", "hon nhan", "cuoc song", "doi song", "am thuc", "mon an", "nau an",
            "tinh yeu", "nguoi gia", "tre em", "lifestyle", "family", "relationships", "recipe"
        }),
        new("xe", "Xe", "Ô tô, xe máy, xe điện, giao thông người lái xe.", new[]
        {
            "o to", "oto", "xe may", "xe dien", "vinfast", "toyota", "honda", "hyundai", "mazda", "mercedes",
            "bmw", "dang kiem", "bang lai", "giay phep lai xe", "phat nguoi", "byd", "tesla", "electric vehicle",
            "electric vehicles", "automaker", "carmaker", "motorbike"
        }),
        new("du-lich", "Du lịch", "Điểm đến, hàng không, khách sạn, kinh nghiệm du lịch.", new[]
        {
            "du lich", "du khach", "khach du lich", "khach san", "resort", "diem den", "check in", "tour", "ve may bay",
            "hang khong", "vietnam airlines", "vietjet", "bai bien", "visa", "travel", "tourism", "tourists", "airline",
            "airlines", "hotel", "destination"
        }),
        new("khoa-hoc", "Khoa học", "Vũ trụ, phát hiện mới, môi trường, nghiên cứu.", new[]
        {
            "khoa hoc", "nha khoa hoc", "nghien cuu", "vu tru", "nasa", "thien van", "hanh tinh", "thien thach",
            "khung long", "hoa thach", "khao co", "bien doi khi hau", "science", "scientists", "researchers",
            "study finds", "space", "planet", "asteroid", "climate change", "fossil"
        }),
        new(Custom, "Tự chọn từ khoá", "Chỉ lấy tin chứa các từ khoá bạn nhập cho Page này.", Array.Empty<string>()),
    };

    private static readonly Dictionary<string, NewsTopic> ByKey = All.ToDictionary(t => t.Key);

    public static NewsTopic? Find(string? key) => key != null && ByKey.TryGetValue(key, out var t) ? t : null;

    public static string NameOf(string? key) => Find(key)?.Name ?? key ?? string.Empty;

    /// <summary>Topics of one story: its section feed's topic plus every topic whose keywords it contains.</summary>
    public static List<string> Classify(string title, string? summary, string? feedTopic)
    {
        var result = new List<string>();
        if (!string.IsNullOrEmpty(feedTopic)) result.Add(feedTopic);
        var text = title + " " + summary;
        var folded = " " + Fold(text) + " ";
        foreach (var topic in All)
        {
            if (topic.Keywords.Length == 0 || result.Contains(topic.Key)) continue;
            if (ContainsAny(text, folded, topic.Keywords)) result.Add(topic.Key);
        }
        return result;
    }

    /// <summary>True when the text contains one of the editor's keywords (accents and case ignored).</summary>
    public static bool MatchesKeywords(string title, string? summary, IReadOnlyCollection<string> keywords)
    {
        if (keywords.Count == 0) return false;
        var text = title + " " + summary;
        return ContainsAny(text, " " + Fold(text) + " ", keywords.Select(k => Fold(k).Trim()).Where(k => k.Length > 0));
    }

    public static List<string> ParseKeywords(string? value) => (value ?? string.Empty)
        .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(k => k.Trim())
        .Where(k => k.Length >= 2)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(50)
        .ToList();

    private static bool ContainsAny(string original, string foldedPadded, IEnumerable<string> keywords)
    {
        foreach (var k in keywords)
        {
            if (k.StartsWith('='))
            {
                if (Regex.IsMatch(original, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(k[1..])}(?![\p{{L}}\p{{N}}])")) return true;
            }
            else if (foldedPadded.Contains(" " + k + " ", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Lower-case, no accents (đ → d), punctuation turned into single spaces.</summary>
    public static string Fold(string text)
    {
        var normalized = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var lastSpace = true;
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var ch = c == 'đ' ? 'd' : c;
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastSpace = false;
            }
            else if (!lastSpace)
            {
                sb.Append(' ');
                lastSpace = true;
            }
        }
        return sb.ToString().Trim();
    }
}
