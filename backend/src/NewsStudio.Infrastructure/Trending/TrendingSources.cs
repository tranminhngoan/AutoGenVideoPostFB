namespace NewsStudio.Infrastructure.Trending;

/// <param name="Readable">
/// False when this machine can list the feed but cannot open the articles (site blocked in Vietnam or
/// anti-bot 403). Such outlets still count towards "how many outlets cover this story", but a readable
/// outlet's article is shown as the card so "Tạo video" always works.
/// </param>
public record FeedSource(string Key, string Name, string Url, string Region, string Language, bool Featured = false, bool Readable = true);

/// <summary>
/// Public RSS feeds used for "Tin đang hot". All were verified reachable on 2026-11-15.
/// Extra feeds can be added without code changes via configuration:
///   "Trending": { "ExtraFeeds": [ { "Key": "...", "Name": "...", "Url": "...", "Region": "VN", "Language": "vi" } ] }
/// </summary>
public static class TrendingSources
{
    public static readonly IReadOnlyList<FeedSource> Feeds = new List<FeedSource>
    {
        // Việt Nam
        new("vnexpress", "VnExpress", "https://vnexpress.net/rss/tin-moi-nhat.rss", "VN", "vi"),
        new("vnexpress", "VnExpress", "https://vnexpress.net/rss/tin-noi-bat.rss", "VN", "vi", Featured: true),
        new("tuoitre", "Tuổi Trẻ", "https://tuoitre.vn/rss/tin-moi-nhat.rss", "VN", "vi"),
        new("thanhnien", "Thanh Niên", "https://thanhnien.vn/rss/home.rss", "VN", "vi"),
        new("dantri", "Dân Trí", "https://dantri.com.vn/rss/home.rss", "VN", "vi"),
        new("nld", "Người Lao Động", "https://nld.com.vn/rss/home.rss", "VN", "vi"),
        new("vtv", "VTV", "https://vtv.vn/rss/home.rss", "VN", "vi"),
        new("vietnamnet", "VietNamNet", "https://vietnamnet.vn/tin-tuc-24h.rss", "VN", "vi"),
        new("soha", "Soha", "https://soha.vn/rss/home.rss", "VN", "vi"),
        new("cafef", "CafeF", "https://cafef.vn/home.rss", "VN", "vi"),
        new("bbcvn", "BBC Tiếng Việt", "https://feeds.bbci.co.uk/vietnamese/rss.xml", "VN", "vi", Readable: false), // bbc.com blocked in VN

        // Quốc tế
        new("bbc", "BBC News", "https://feeds.bbci.co.uk/news/world/rss.xml", "World", "en", Featured: true, Readable: false), // bbc.co.uk blocked in VN
        new("guardian", "The Guardian", "https://www.theguardian.com/world/rss", "World", "en"),
        new("aljazeera", "Al Jazeera", "https://www.aljazeera.com/xml/rss/all.xml", "World", "en"),
        new("nyt", "The New York Times", "https://rss.nytimes.com/services/xml/rss/nyt/World.xml", "World", "en"),
        new("npr", "NPR", "https://feeds.npr.org/1004/rss.xml", "World", "en"),
        new("cnbc", "CNBC", "https://www.cnbc.com/id/100727362/device/rss/rss.html", "World", "en"),
        new("skynews", "Sky News", "https://feeds.skynews.com/feeds/rss/world.xml", "World", "en", Readable: false), // 403 to automated readers
        new("france24", "France 24", "https://www.france24.com/en/rss", "World", "en", Readable: false), // 403 to automated readers
        new("dw", "DW", "https://rss.dw.com/rdf/rss-en-all", "World", "en", Readable: false), // 403 to automated readers
    };

    /// <summary>Google Trends daily search trends — the "what people are searching right now" signal.</summary>
    public static readonly IReadOnlyList<(string Geo, string Url)> TrendFeeds = new List<(string, string)>
    {
        ("VN", "https://trends.google.com/trending/rss?geo=VN"),
        ("US", "https://trends.google.com/trending/rss?geo=US"),
    };
}
