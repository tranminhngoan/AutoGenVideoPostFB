using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NewsStudio.Infrastructure.Trending;

/// <param name="Keywords">
/// Lower-case, accent-free phrases matched on whole words in the title + summary of stories from
/// general feeds (home pages). A phrase starting with "=" is matched case-sensitively on the original
/// text (e.g. "=AI", so the Vietnamese word "ai" = "who" doesn't count). A phrase starting with "~" is a
/// regular expression on the folded text (e.g. "~euro 20\d\d" — the tournament, not the currency).
/// </param>
/// <param name="Exclude">
/// Regular expressions on the folded text whose matches are ignored before looking for keywords, for
/// phrases that use a topic word about something else ("dài hơn 3 sân bóng đá", "dự án sân golf").
/// </param>
/// <param name="TitleOnly">
/// Keywords are looked for in the headline only: for a narrow topic (forex) where a passing mention in
/// the summary ("... quy đổi theo tỷ giá ...") would pull in unrelated business stories.
/// </param>
public record NewsTopic(string Key, string Name, string Description, string[] Keywords, bool TitleOnly = false, string[]? Exclude = null);

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
    /// <summary>
    /// Viral stories: matched to a search that is trending on Google right now (VN / US), or carried by
    /// many outlets at once. Decided when listing (from the trend + coverage signals), not by keywords.
    /// </summary>
    public const string HotTrend = "hot-trend";
    /// <summary>Outlets covering one story from which it counts as "hot trend" even without a Google trend.</summary>
    public const int HotTrendMinOutlets = 4;

    public static readonly IReadOnlyList<NewsTopic> All = new List<NewsTopic>
    {
        new(General, "Tổng hợp", "Mọi tin hot nhất, không giới hạn chủ đề.", Array.Empty<string>()),
        new(HotTrend, "Hot trend", "Tin đang được tìm nhiều trên Google (Việt Nam, Mỹ) hoặc được từ 4 báo trở lên cùng đưa, mọi lĩnh vực.", Array.Empty<string>()),
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
        new("forex", "Forex", "Ngoại hối, tỷ giá, đồng USD, Fed và ngân hàng trung ương, vàng, dầu thế giới.", new[]
        {
            "forex", "ngoai hoi", "ngoai te", "ty gia", "dong usd", "dong do la", "do la my", "dong bac xanh", "chi so usd", "dxy",
            "usd vnd", "eur usd", "gbp usd", "usd jpy", "aud usd", "nzd usd", "usd cad", "usd chf", "usd cny", "xau usd",
            "dong euro", "dong yen", "yen nhat", "dong bang anh", "nhan dan te", "fed", "cuc du tru lien bang",
            "ngan hang trung uong", "lai suat dieu hanh", "ecb", "boj", "pboc", "vang the gioi", "gia vang the gioi",
            "dau tho", "=FX", "=yen", "=Yen", "dollar", "currency", "currencies", "central bank", "federal reserve",
            "rate cut", "rate cuts", "rate hike", "rate hikes", "treasury yields", "bond yields", "nonfarm", "payrolls",
            "gold price", "gold prices", "crude oil", "oil prices", "brent", "wti", "sterling", "yuan"
        }, TitleOnly: true),
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
            // "euro" alone is mostly the currency ("1,7 tỷ Euro"); the tournament is "EURO" or "Euro 2028".
            // "olympic" alone is also "Olympic Toán", "Olympic dọn phòng": only the Games count.
            "bong da", "doi tuyen", "tuyen viet nam", "v league", "sea games", "asiad", "world cup", "=EURO", "vck euro",
            "~euro 20\\d\\d", "ngoai hang anh", "premier league", "champions league", "la liga", "hlv", "cau thu", "ban thang",
            "tennis", "quan vot", "the van hoi", "olympic games", "~olympic (?:20\\d\\d|paris|tokyo|mua he|mua dong)", "=Olympics",
            "vo dich", "football", "soccer", "nba", "f1", "formula 1", "golf", "boxing", "man utd",
            "man city", "arsenal", "liverpool", "chelsea", "real madrid", "barcelona", "messi", "ronaldo"
        }, Exclude: new[]
        {
            // size comparisons and real estate, not sport
            @" (?:\d+|mot|hai|ba|bon|nam|sau|bay|tam|chin|muoi|vai|nhieu|bang|hon) san bong da", @" san golf", @" ca do bong da"
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
        var foldedTitle = " " + Fold(title) + " ";
        foreach (var topic in All)
        {
            if (topic.Keywords.Length == 0 || result.Contains(topic.Key)) continue;
            var hit = topic.TitleOnly
                ? ContainsAny(title, WithoutExcluded(foldedTitle, topic.Exclude), topic.Keywords)
                : ContainsAny(text, WithoutExcluded(folded, topic.Exclude), topic.Keywords);
            if (hit) result.Add(topic.Key);
        }
        return result;
    }

    /// <summary>
    /// True when the text contains one of the editor's keywords, as whole words:
    /// <list type="bullet">
    /// <item>an all-capitals keyword ("AI", "FX", "XAUUSD") must appear in capitals — "AI" is not "ai" (= who);</item>
    /// <item>a keyword typed with Vietnamese accents must match its accents ("vàng" ≠ "vắng"), case ignored;</item>
    /// <item>a keyword typed without accents matches with or without them.</item>
    /// </list>
    /// </summary>
    public static bool MatchesKeywords(string title, string? summary, IReadOnlyCollection<string> keywords)
    {
        if (keywords.Count == 0) return false;
        var text = title + " " + summary;
        var folded = " " + Fold(text) + " ";
        foreach (var raw in keywords)
        {
            var k = raw.Trim();
            if (k.Length == 0) continue;
            var plain = Fold(k);
            if (plain.Length == 0) continue;
            bool hit;
            if (k.Length <= 8 && k.Any(char.IsLetter) && k.All(ch => char.IsUpper(ch) || char.IsDigit(ch)))
                hit = Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(k)}(?![\p{{L}}\p{{N}}])");
            else if (!string.Equals(plain, Regex.Replace(k.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim(), StringComparison.Ordinal))
                hit = AccentedPhraseIn(text, k);
            else
                hit = folded.Contains(" " + plain + " ", StringComparison.Ordinal);
            if (hit) return true;
        }
        return false;
    }

    /// <summary>
    /// Whole-word phrase match where each word must have the keyword's accents, unless the text wrote that
    /// word without any accent ("BONG DA" for "bóng đá"): "vàng" matches "Vàng" and "VANG", never "vắng".
    /// </summary>
    private static bool AccentedPhraseIn(string text, string keyword)
    {
        static string[] Words(string s) => Regex.Split(s.Normalize(NormalizationForm.FormC).ToLowerInvariant(), @"[^\p{L}\p{N}]+")
            .Where(w => w.Length > 0).ToArray();
        var words = Words(text);
        var key = Words(keyword);
        if (key.Length == 0) return false;
        static bool Same(string word, string k) => word == k || (Fold(word) == word && Fold(k) == word);
        for (var i = 0; i + key.Length <= words.Length; i++)
        {
            var all = true;
            for (var j = 0; j < key.Length && all; j++) all = Same(words[i + j], key[j]);
            if (all) return true;
        }
        return false;
    }

    private static string WithoutExcluded(string folded, string[]? exclude)
    {
        if (exclude == null) return folded;
        foreach (var pattern in exclude) folded = Regex.Replace(folded, pattern, " ");
        return folded;
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
            else if (k.StartsWith('~'))
            {
                if (Regex.IsMatch(foldedPadded, $@" {k[1..]} ")) return true;
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
