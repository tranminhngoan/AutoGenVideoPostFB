namespace NewsStudio.Infrastructure.Trending;

/// <param name="Readable">
/// False when this machine can list the feed but cannot open the articles (site blocked in Vietnam or
/// anti-bot 403). Such outlets still count towards "how many outlets cover this story", but a readable
/// outlet's article is shown as the card so "Tạo video" always works.
/// </param>
/// <param name="Topic">Section feed's topic (see <see cref="NewsTopics"/>); null for home / latest-news feeds.</param>
public record FeedSource(string Key, string Name, string Url, string Region, string Language, bool Featured = false, bool Readable = true, string? Topic = null);

/// <summary>
/// Public RSS feeds used for "Tin đang hot". All were verified reachable on 2026-11-15 (forex feeds 2026-11-16).
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
        new("cafef", "CafeF", "https://cafef.vn/home.rss", "VN", "vi"), // home page mixes every topic: classified by keywords
        new("bbcvn", "BBC Tiếng Việt", "https://feeds.bbci.co.uk/vietnamese/rss.xml", "VN", "vi", Readable: false), // bbc.com blocked in VN

        // Quốc tế
        new("bbc", "BBC News", "https://feeds.bbci.co.uk/news/world/rss.xml", "World", "en", Featured: true, Readable: false, Topic: "the-gioi"), // bbc.co.uk blocked in VN
        new("guardian", "The Guardian", "https://www.theguardian.com/world/rss", "World", "en", Topic: "the-gioi"),
        new("aljazeera", "Al Jazeera", "https://www.aljazeera.com/xml/rss/all.xml", "World", "en", Topic: "the-gioi"),
        new("nyt", "The New York Times", "https://rss.nytimes.com/services/xml/rss/nyt/World.xml", "World", "en", Topic: "the-gioi"),
        new("npr", "NPR", "https://feeds.npr.org/1004/rss.xml", "World", "en", Topic: "the-gioi"),
        new("cnbc", "CNBC", "https://www.cnbc.com/id/100727362/device/rss/rss.html", "World", "en", Topic: "the-gioi"),
        new("skynews", "Sky News", "https://feeds.skynews.com/feeds/rss/world.xml", "World", "en", Readable: false, Topic: "the-gioi"), // 403 to automated readers
        new("france24", "France 24", "https://www.france24.com/en/rss", "World", "en", Readable: false), // 403 to automated readers
        new("dw", "DW", "https://rss.dw.com/rdf/rss-en-all", "World", "en", Readable: false), // 403 to automated readers

        // Chuyên mục theo chủ đề (cho các Page chuyên một chủ đề) — verified reachable on 2026-11-16
        // thoi-su
        new("vnexpress", "VnExpress", "https://vnexpress.net/rss/thoi-su.rss", "VN", "vi", Topic: "thoi-su"),
        new("tuoitre", "Tuổi Trẻ", "https://tuoitre.vn/rss/thoi-su.rss", "VN", "vi", Topic: "thoi-su"),
        new("thanhnien", "Thanh Niên", "https://thanhnien.vn/rss/thoi-su.rss", "VN", "vi", Topic: "thoi-su"),
        // the-gioi
        new("vnexpress", "VnExpress", "https://vnexpress.net/rss/the-gioi.rss", "VN", "vi", Topic: "the-gioi"),
        new("tuoitre", "Tuổi Trẻ", "https://tuoitre.vn/rss/the-gioi.rss", "VN", "vi", Topic: "the-gioi"),
        new("thanhnien", "Thanh Niên", "https://thanhnien.vn/rss/the-gioi.rss", "VN", "vi", Topic: "the-gioi"),
        // kinh-doanh
        new("vnexpress", "VnExpress", "https://vnexpress.net/rss/kinh-doanh.rss", "VN", "vi", Topic: "kinh-doanh"),
        new("tuoitre", "Tuổi Trẻ", "https://tuoitre.vn/rss/kinh-doanh.rss", "VN", "vi", Topic: "kinh-doanh"),
        new("thanhnien", "Thanh Niên", "https://thanhnien.vn/rss/kinh-te.rss", "VN", "vi", Topic: "kinh-doanh"),
        new("dantri", "Dân Trí", "https://dantri.com.vn/rss/kinh-doanh.rss", "VN", "vi", Topic: "kinh-doanh"),
        new("guardian", "The Guardian", "https://www.theguardian.com/uk/business/rss", "World", "en", Topic: "kinh-doanh"),
        new("nyt", "The New York Times", "https://rss.nytimes.com/services/xml/rss/nyt/Business.xml", "World", "en", Topic: "kinh-doanh"),
        new("npr", "NPR", "https://feeds.npr.org/1006/rss.xml", "World", "en", Topic: "kinh-doanh"),
        new("cnbc", "CNBC", "https://www.cnbc.com/id/10001147/device/rss/rss.html", "World", "en", Topic: "kinh-doanh"),
        // bat-dong-san
        new("vnexpress", "VnExpress", "https://vnexpress.net/rss/bat-dong-san.rss", "VN", "vi", Topic: "bat-dong-san"),
        new("nyt", "The New York Times", "https://rss.nytimes.com/services/xml/rss/nyt/RealEstate.xml", "World", "en", Topic: "bat-dong-san"),
        // cong-nghe
        new("vnexpress", "VnExpress", "https://vnexpress.net/rss/so-hoa.rss", "VN", "vi", Topic: "cong-nghe"),
        new("tuoitre", "Tuổi Trẻ", "https://tuoitre.vn/rss/cong-nghe.rss", "VN", "vi", Topic: "cong-nghe"),
        new("thanhnien", "Thanh Niên", "https://thanhnien.vn/rss/cong-nghe.rss", "VN", "vi", Topic: "cong-nghe"),
        new("dantri", "Dân Trí", "https://dantri.com.vn/rss/cong-nghe.rss", "VN", "vi", Topic: "cong-nghe"),
        new("guardian", "The Guardian", "https://www.theguardian.com/uk/technology/rss", "World", "en", Topic: "cong-nghe"),
        new("nyt", "The New York Times", "https://rss.nytimes.com/services/xml/rss/nyt/Technology.xml", "World", "en", Topic: "cong-nghe"),
        new("npr", "NPR", "https://feeds.npr.org/1019/rss.xml", "World", "en", Topic: "cong-nghe"),
        new("cnbc", "CNBC", "https://www.cnbc.com/id/19854910/device/rss/rss.html", "World", "en", Topic: "cong-nghe"),
        // the-thao
        new("vnexpress", "VnExpress", "https://vnexpress.net/rss/the-thao.rss", "VN", "vi", Topic: "the-thao"),
        new("tuoitre", "Tuổi Trẻ", "https://tuoitre.vn/rss/the-thao.rss", "VN", "vi", Topic: "the-thao"),
        new("thanhnien", "Thanh Niên", "https://thanhnien.vn/rss/the-thao.rss", "VN", "vi", Topic: "the-thao"),
        new("dantri", "Dân Trí", "https://dantri.com.vn/rss/the-thao.rss", "VN", "vi", Topic: "the-thao"),
        new("guardian", "The Guardian", "https://www.theguardian.com/uk/sport/rss", "World", "en", Topic: "the-thao"),
        new("guardian", "The Guardian", "https://www.theguardian.com/football/rss", "World", "en", Topic: "the-thao"),
        new("npr", "NPR", "https://feeds.npr.org/1055/rss.xml", "World", "en", Topic: "the-thao"),
        // giai-tri
        new("vnexpress", "VnExpress", "https://vnexpress.net/rss/giai-tri.rss", "VN", "vi", Topic: "giai-tri"),
        new("tuoitre", "Tuổi Trẻ", "https://tuoitre.vn/rss/giai-tri.rss", "VN", "vi", Topic: "giai-tri"),
        new("thanhnien", "Thanh Niên", "https://thanhnien.vn/rss/giai-tri.rss", "VN", "vi", Topic: "giai-tri"),
        new("dantri", "Dân Trí", "https://dantri.com.vn/rss/giai-tri.rss", "VN", "vi", Topic: "giai-tri"),
        new("guardian", "The Guardian", "https://www.theguardian.com/uk/culture/rss", "World", "en", Topic: "giai-tri"),
        new("nyt", "The New York Times", "https://rss.nytimes.com/services/xml/rss/nyt/Arts.xml", "World", "en", Topic: "giai-tri"),
        new("npr", "NPR", "https://feeds.npr.org/1008/rss.xml", "World", "en", Topic: "giai-tri"),
        // suc-khoe
        new("vnexpress", "VnExpress", "https://vnexpress.net/rss/suc-khoe.rss", "VN", "vi", Topic: "suc-khoe"),
        new("tuoitre", "Tuổi Trẻ", "https://tuoitre.vn/rss/suc-khoe.rss", "VN", "vi", Topic: "suc-khoe"),
        new("thanhnien", "Thanh Niên", "https://thanhnien.vn/rss/suc-khoe.rss", "VN", "vi", Topic: "suc-khoe"),
        new("dantri", "Dân Trí", "https://dantri.com.vn/rss/suc-khoe.rss", "VN", "vi", Topic: "suc-khoe"),
        new("guardian", "The Guardian", "https://www.theguardian.com/society/health/rss", "World", "en", Topic: "suc-khoe"),
        new("nyt", "The New York Times", "https://rss.nytimes.com/services/xml/rss/nyt/Health.xml", "World", "en", Topic: "suc-khoe"),
        new("npr", "NPR", "https://feeds.npr.org/1128/rss.xml", "World", "en", Topic: "suc-khoe"),
        // giao-duc
        new("vnexpress", "VnExpress", "https://vnexpress.net/rss/giao-duc.rss", "VN", "vi", Topic: "giao-duc"),
        new("tuoitre", "Tuổi Trẻ", "https://tuoitre.vn/rss/giao-duc.rss", "VN", "vi", Topic: "giao-duc"),
        new("thanhnien", "Thanh Niên", "https://thanhnien.vn/rss/giao-duc.rss", "VN", "vi", Topic: "giao-duc"),
        new("dantri", "Dân Trí", "https://dantri.com.vn/rss/giao-duc.rss", "VN", "vi", Topic: "giao-duc"),
        new("guardian", "The Guardian", "https://www.theguardian.com/education/rss", "World", "en", Topic: "giao-duc"),
        new("nyt", "The New York Times", "https://rss.nytimes.com/services/xml/rss/nyt/Education.xml", "World", "en", Topic: "giao-duc"),
        // forex: dedicated FX / central bank / commodities desks, plus Vietnamese finance sections
        // whose FX stories (tỷ giá, Fed, vàng thế giới...) are picked by the topic's keywords
        new("investinglive", "investingLive (ForexLive)", "https://investinglive.com/feed/forex/", "World", "en", Topic: "forex"),
        new("investinglive", "investingLive (ForexLive)", "https://investinglive.com/feed/centralbank/", "World", "en", Topic: "forex"),
        new("investinglive", "investingLive (ForexLive)", "https://investinglive.com/feed/commodities/", "World", "en", Topic: "forex"),
        new("investinglive", "investingLive (ForexLive)", "https://investinglive.com/feed/news/", "World", "en"),
        new("fxempire", "FXEmpire", "https://www.fxempire.com/api/v1/en/articles/rss/forecasts", "World", "en", Topic: "forex"),
        new("vneconomy", "VnEconomy", "https://vneconomy.vn/tai-chinh.rss", "VN", "vi", Topic: "kinh-doanh"),
        new("cafef", "CafeF", "https://cafef.vn/tai-chinh-quoc-te.rss", "VN", "vi"),
        new("vietstock", "Vietstock", "https://vietstock.vn/773/the-gioi/tai-chinh-quoc-te.rss", "VN", "vi"),
        new("tuoitre", "Tuổi Trẻ", "https://tuoitre.vn/rss/tai-chinh.rss", "VN", "vi", Topic: "kinh-doanh"),
        // phap-luat
        new("vnexpress", "VnExpress", "https://vnexpress.net/rss/phap-luat.rss", "VN", "vi", Topic: "phap-luat"),
        new("tuoitre", "Tuổi Trẻ", "https://tuoitre.vn/rss/phap-luat.rss", "VN", "vi", Topic: "phap-luat"),
        new("dantri", "Dân Trí", "https://dantri.com.vn/rss/phap-luat.rss", "VN", "vi", Topic: "phap-luat"),
        new("guardian", "The Guardian", "https://www.theguardian.com/law/rss", "World", "en", Topic: "phap-luat"),
        // doi-song
        new("vnexpress", "VnExpress", "https://vnexpress.net/rss/gia-dinh.rss", "VN", "vi", Topic: "doi-song"),
        new("tuoitre", "Tuổi Trẻ", "https://tuoitre.vn/rss/nhip-song-tre.rss", "VN", "vi", Topic: "doi-song"),
        new("thanhnien", "Thanh Niên", "https://thanhnien.vn/rss/doi-song.rss", "VN", "vi", Topic: "doi-song"),
        new("dantri", "Dân Trí", "https://dantri.com.vn/rss/doi-song.rss", "VN", "vi", Topic: "doi-song"),
        new("guardian", "The Guardian", "https://www.theguardian.com/lifeandstyle/rss", "World", "en", Topic: "doi-song"),
        // xe
        new("vnexpress", "VnExpress", "https://vnexpress.net/rss/oto-xe-may.rss", "VN", "vi", Topic: "xe"),
        new("tuoitre", "Tuổi Trẻ", "https://tuoitre.vn/rss/xe.rss", "VN", "vi", Topic: "xe"),
        new("thanhnien", "Thanh Niên", "https://thanhnien.vn/rss/xe.rss", "VN", "vi", Topic: "xe"),
        new("dantri", "Dân Trí", "https://dantri.com.vn/rss/o-to-xe-may.rss", "VN", "vi", Topic: "xe"),
        new("guardian", "The Guardian", "https://www.theguardian.com/technology/motoring/rss", "World", "en", Topic: "xe"),
        new("nyt", "The New York Times", "https://rss.nytimes.com/services/xml/rss/nyt/Automobiles.xml", "World", "en", Topic: "xe"),
        // du-lich
        new("vnexpress", "VnExpress", "https://vnexpress.net/rss/du-lich.rss", "VN", "vi", Topic: "du-lich"),
        new("tuoitre", "Tuổi Trẻ", "https://tuoitre.vn/rss/du-lich.rss", "VN", "vi", Topic: "du-lich"),
        new("thanhnien", "Thanh Niên", "https://thanhnien.vn/rss/du-lich.rss", "VN", "vi", Topic: "du-lich"),
        new("dantri", "Dân Trí", "https://dantri.com.vn/rss/du-lich.rss", "VN", "vi", Topic: "du-lich"),
        new("guardian", "The Guardian", "https://www.theguardian.com/uk/travel/rss", "World", "en", Topic: "du-lich"),
        new("nyt", "The New York Times", "https://rss.nytimes.com/services/xml/rss/nyt/Travel.xml", "World", "en", Topic: "du-lich"),
        // khoa-hoc
        new("vnexpress", "VnExpress", "https://vnexpress.net/rss/khoa-hoc.rss", "VN", "vi", Topic: "khoa-hoc"),
        new("tuoitre", "Tuổi Trẻ", "https://tuoitre.vn/rss/khoa-hoc.rss", "VN", "vi", Topic: "khoa-hoc"),
        new("guardian", "The Guardian", "https://www.theguardian.com/science/rss", "World", "en", Topic: "khoa-hoc"),
        new("nyt", "The New York Times", "https://rss.nytimes.com/services/xml/rss/nyt/Science.xml", "World", "en", Topic: "khoa-hoc"),
        new("npr", "NPR", "https://feeds.npr.org/1007/rss.xml", "World", "en", Topic: "khoa-hoc"),
    };

    /// <summary>Google Trends daily search trends — the "what people are searching right now" signal.</summary>
    public static readonly IReadOnlyList<(string Geo, string Url)> TrendFeeds = new List<(string, string)>
    {
        ("VN", "https://trends.google.com/trending/rss?geo=VN"),
        ("US", "https://trends.google.com/trending/rss?geo=US"),
    };
}
