using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Infrastructure.Trending;

/// <summary>
/// Builds the "Tin đang hot" list from public RSS feeds.
///
/// "Hot" combines four real signals (no invented popularity numbers):
///   1. Coverage — how many different outlets publish the same story (title-similarity clustering).
///   2. Search demand — the story matches a Google Trends search term (with its approximate volume).
///   3. Editorial weight — it appears in an outlet's curated "featured / top stories" feed.
///   4. Freshness — the score halves every 10 hours.
/// </summary>
public class TrendingNewsService : ITrendingNewsService
{
    private const int KeepHours = 72;
    private const double HalfLifeHours = 10.0;
    private const string LastRefreshKey = "Trending:LastRefresh";
    private const string KeywordsKey = "Trending:Keywords";
    private const string UserAgent = "Mozilla/5.0 (compatible; TramTin247-NewsReader/1.0)";

    private static readonly Regex WordRegex = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex ImgRegex = new(@"<img[^>]+src=[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SpaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex NonArticleUrl = new(
        @"/(video|videos|live|audio|podcasts?|tv-shows|programmes|sounds|gallery|galleries|picture|pictures)(/|$|-)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Outlets whose articles cannot be opened from here (blocked / anti-bot) — ranking signal only.</summary>
    private static HashSet<string> UnreadableKeys(IEnumerable<FeedSource> feeds) =>
        feeds.GroupBy(f => f.Key).Where(g => g.All(f => !f.Readable)).Select(g => g.Key).ToHashSet();

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Vietnamese
        "và","của","là","có","cho","với","các","những","một","được","trong","khi","đã","sẽ","người","không","này",
        "để","từ","tại","về","ra","vào","sau","trên","năm","ngày","bị","đến","như","thì","mà","hơn","nhiều","lại",
        "đang","nhất","theo","vì","do","gì","nào","sao","thế","rất","còn","cũng","chỉ","hay","hoặc","nhưng","nên",
        "tin","mới","việc","làm","đi","năm","tháng","giờ","phút","bao","gần","hai","ba","chưa","sắp",
        // English
        "the","a","an","of","to","in","on","for","and","is","are","as","at","by","with","from","after","over","says",
        "said","new","be","was","it","its","his","her","has","have","will","not","but","that","this","than","into",
        "up","out","what","who","how","why","after","amid","more","could","would","about","their","they","we","you",
        "live","latest","news","video","watch","us","uk"
    };

    private readonly HttpClient _httpClient;
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TrendingNewsService> _logger;

    public TrendingNewsService(
        HttpClient httpClient,
        IApplicationDbContext context,
        IConfiguration configuration,
        ILogger<TrendingNewsService> logger)
    {
        _httpClient = httpClient;
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    /* ───────────────────────────── Refresh ───────────────────────────── */

    public async Task<TrendingRefreshResultDto> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var result = new TrendingRefreshResultDto { StartedAtUtc = DateTime.UtcNow };
        var feeds = GetFeeds();

        // 1. Fetch every feed in parallel (bounded)
        using var gate = new SemaphoreSlim(6);
        var fetchTasks = feeds.Select(async feed =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var xml = await DownloadAsync(feed.Url, cancellationToken);
                return (feed, items: ParseFeed(xml, feed), error: (string?)null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return (feed, items: new List<ParsedItem>(), error: ex.Message);
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        var fetched = await Task.WhenAll(fetchTasks);
        foreach (var f in fetched)
        {
            if (f.error == null && f.items.Count > 0) result.FeedsOk++;
            else
            {
                result.FeedsFailed++;
                result.FailedFeeds.Add($"{f.feed.Name}: {f.error ?? "không có tin"}");
                _logger.LogWarning("Trending feed {Feed} failed: {Error}", f.feed.Url, f.error ?? "empty");
            }
        }

        var trends = await FetchTrendsAsync(cancellationToken);
        result.TrendKeywords = trends.Count;

        // 2. Keep recent items, de-duplicate by URL (the same story can be in "latest" and "featured")
        var now = DateTime.UtcNow;
        var cutoff = now.AddHours(-KeepHours);
        var items = fetched.SelectMany(f => f.items)
            .Where(i => i.PublishedAtUtc >= cutoff)
            .GroupBy(i => i.Url)
            .Select(g => g.OrderByDescending(i => i.Featured).First() with { Featured = g.Any(i => i.Featured) })
            .ToList();
        result.ItemsFetched = items.Count;

        // 3. Upsert
        var urls = items.Select(i => i.Url).ToList();
        var existing = await _context.TrendingArticles
            .Where(a => urls.Contains(a.Url))
            .ToDictionaryAsync(a => a.Url, cancellationToken);

        foreach (var item in items)
        {
            if (existing.TryGetValue(item.Url, out var row))
            {
                row.Title = item.Title;
                row.Summary = item.Summary ?? row.Summary;
                row.ImageUrl = item.ImageUrl ?? row.ImageUrl;
                row.IsFeatured |= item.Featured;
                row.LastSeenAtUtc = now;
                row.UpdatedAtUtc = now;
            }
            else
            {
                _context.TrendingArticles.Add(new TrendingArticle
                {
                    Url = item.Url,
                    Title = item.Title,
                    Summary = item.Summary,
                    ImageUrl = item.ImageUrl,
                    SourceKey = item.Feed.Key,
                    SourceName = item.Feed.Name,
                    Region = item.Feed.Region,
                    Language = item.Feed.Language,
                    PublishedAtUtc = item.PublishedAtUtc,
                    LastSeenAtUtc = now,
                    IsFeatured = item.Featured
                });
                result.NewItems++;
            }
        }

        // Drop stories that are too old to be "hot"
        var stale = await _context.TrendingArticles.Where(a => a.PublishedAtUtc < cutoff).ToListAsync(cancellationToken);
        foreach (var s in stale) _context.TrendingArticles.Remove(s);
        await _context.SaveChangesAsync(cancellationToken);

        // 4. Recompute story clusters + trend matches over everything still in the window
        var rows = await _context.TrendingArticles.Where(a => a.PublishedAtUtc >= cutoff).ToListAsync(cancellationToken);
        Cluster(rows, feeds);
        MatchTrends(rows, trends);
        result.TotalStored = rows.Count;
        result.FinishedAtUtc = DateTime.UtcNow;

        await SaveSettingAsync(LastRefreshKey, JsonSerializer.Serialize(result), cancellationToken);
        await SaveSettingAsync(KeywordsKey, JsonSerializer.Serialize(
            trends.OrderByDescending(t => t.Traffic).Select(t => t.Keyword).Where(k => k.Trim().Length >= 2).Distinct().Take(20).ToList()), cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Trending refresh: {Ok}/{Total} feeds ok, {Fetched} items ({New} new), {Trends} trend terms, {Stored} stored",
            result.FeedsOk, feeds.Count, result.ItemsFetched, result.NewItems, result.TrendKeywords, result.TotalStored);
        return result;
    }

    private List<FeedSource> GetFeeds()
    {
        var feeds = TrendingSources.Feeds.ToList();
        foreach (var extra in _configuration.GetSection("Trending:ExtraFeeds").GetChildren())
        {
            var url = extra["Url"];
            if (string.IsNullOrWhiteSpace(url)) continue;
            feeds.Add(new FeedSource(
                extra["Key"] ?? new Uri(url).Host,
                extra["Name"] ?? new Uri(url).Host,
                url,
                extra["Region"] ?? "VN",
                extra["Language"] ?? "vi",
                extra.GetValue<bool>("Featured")));
        }
        return feeds;
    }

    private async Task<string> DownloadAsync(string url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        using var response = await _httpClient.SendAsync(request, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
        // Strip a UTF-8 BOM / leading junk — some feeds (e.g. Dân Trí) start with one and XML parsing fails
        return Encoding.UTF8.GetString(bytes).TrimStart('﻿', ' ', '\r', '\n', '\t');
    }

    /* ───────────────────────────── Parsing ───────────────────────────── */

    private record ParsedItem(FeedSource Feed, string Url, string Title, string? Summary, string? ImageUrl, DateTime PublishedAtUtc, bool Featured);

    private static List<ParsedItem> ParseFeed(string xml, FeedSource feed)
    {
        var doc = LoadXml(xml);
        var list = new List<ParsedItem>();
        var now = DateTime.UtcNow;

        foreach (var el in doc.Descendants().Where(e => e.Name.LocalName is "item" or "entry"))
        {
            var title = Clean(Child(el, "title")?.Value);
            var link = ReadLink(el);
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)) continue;
            // Video/audio/live pages have no article text to build a script from
            if (NonArticleUrl.IsMatch(link)) continue;

            var rawDescription = Child(el, "description")?.Value
                                 ?? Child(el, "summary")?.Value
                                 ?? Child(el, "encoded")?.Value
                                 ?? Child(el, "content")?.Value;
            var summary = Clean(rawDescription);
            if (summary != null && summary.Length > 320) summary = summary[..320].TrimEnd() + "…";

            var date = ParseDate(Child(el, "pubDate")?.Value
                                 ?? Child(el, "published")?.Value
                                 ?? Child(el, "updated")?.Value
                                 ?? Child(el, "date")?.Value) ?? now;
            if (date > now) date = now; // feeds with wrong time zones

            list.Add(new ParsedItem(feed, NormalizeUrl(link), title!, summary, ReadImage(el, rawDescription), date, feed.Featured));
        }
        return list;
    }

    private static XDocument LoadXml(string xml)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            return XDocument.Load(reader);
        }
        catch (XmlException)
        {
            // Some feeds contain bare '&' — escape them and retry once.
            var fixedXml = Regex.Replace(xml, @"&(?!(?:[a-zA-Z]+|#\d+|#x[0-9a-fA-F]+);)", "&amp;");
            using var reader = XmlReader.Create(new StringReader(fixedXml), settings);
            return XDocument.Load(reader);
        }
    }

    private static XElement? Child(XElement el, string localName) =>
        el.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    private static string? ReadLink(XElement el)
    {
        var links = el.Elements().Where(e => e.Name.LocalName == "link").ToList();
        foreach (var l in links)
        {
            var href = l.Attribute("href")?.Value;
            var rel = l.Attribute("rel")?.Value;
            if (!string.IsNullOrWhiteSpace(href) && (rel == null || rel == "alternate")) return href.Trim();
            if (!string.IsNullOrWhiteSpace(l.Value)) return l.Value.Trim();
        }
        var guid = Child(el, "guid");
        if (guid != null && guid.Value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return guid.Value.Trim();
        var about = el.Attributes().FirstOrDefault(a => a.Name.LocalName == "about")?.Value; // RSS 1.0 (RDF)
        return about;
    }

    private static string? ReadImage(XElement el, string? descriptionHtml)
    {
        // media:content / media:thumbnail / enclosure — pick the widest image offered
        var candidates = el.Descendants()
            .Where(e => e.Name.LocalName is "content" or "thumbnail" or "enclosure")
            .Select(e => new
            {
                Url = e.Attribute("url")?.Value,
                Type = e.Attribute("type")?.Value ?? e.Attribute("medium")?.Value ?? "image",
                Width = int.TryParse(e.Attribute("width")?.Value, out var w) ? w : 0
            })
            .Where(c => !string.IsNullOrWhiteSpace(c.Url) && c.Type.Contains("image", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Width)
            .ToList();
        if (candidates.Count > 0) return candidates[0].Url;

        if (!string.IsNullOrWhiteSpace(descriptionHtml))
        {
            var m = ImgRegex.Match(descriptionHtml);
            if (m.Success) return WebUtility.HtmlDecode(m.Groups[1].Value);
        }
        return null;
    }

    private static string? Clean(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var text = WebUtility.HtmlDecode(TagRegex.Replace(html, " "));
        text = SpaceRegex.Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = value.Trim();
        s = Regex.Replace(s, @"([+-]\d{2})(\d{2})$", "$1:$2"); // "+0700" → "+07:00"
        s = Regex.Replace(s, @"\s(GMT|UTC|UT|Z)$", " +00:00");
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var dto))
        {
            return dto.UtcDateTime;
        }
        return null;
    }

    private static string NormalizeUrl(string url)
    {
        var u = url.Trim();
        var hash = u.IndexOf('#');
        if (hash >= 0) u = u[..hash];
        var q = u.IndexOf('?');
        if (q >= 0)
        {
            var kept = u[(q + 1)..].Split('&')
                .Where(p => !p.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) && p.Length > 0)
                .ToList();
            u = u[..q] + (kept.Count > 0 ? "?" + string.Join("&", kept) : string.Empty);
        }
        return u.Length > 2000 ? u[..2000] : u;
    }

    /* ───────────────────────────── Google Trends ───────────────────────────── */

    private record TrendTerm(string Keyword, int Traffic, HashSet<string> NewsUrls);

    private async Task<List<TrendTerm>> FetchTrendsAsync(CancellationToken ct)
    {
        var terms = new List<TrendTerm>();
        foreach (var (geo, url) in TrendingSources.TrendFeeds)
        {
            try
            {
                var doc = LoadXml(await DownloadAsync(url, ct));
                foreach (var item in doc.Descendants().Where(e => e.Name.LocalName == "item"))
                {
                    var keyword = Child(item, "title")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(keyword)) continue;
                    var traffic = ParseTraffic(Child(item, "approx_traffic")?.Value);
                    var newsUrls = item.Descendants()
                        .Where(e => e.Name.LocalName == "news_item_url")
                        .Select(e => NormalizeUrl(e.Value))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    terms.Add(new TrendTerm(keyword, traffic, newsUrls));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Google Trends ({Geo}) unavailable: {Message}", geo, ex.Message);
            }
        }
        return terms;
    }

    private static int ParseTraffic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var s = value.Replace("+", "").Replace(",", "").Trim().ToUpperInvariant();
        var mult = s.EndsWith("M") ? 1_000_000 : s.EndsWith("K") ? 1_000 : 1;
        s = s.TrimEnd('M', 'K');
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? (int)Math.Min(int.MaxValue, n * mult) : 0;
    }

    private static void MatchTrends(List<TrendingArticle> rows, List<TrendTerm> trends)
    {
        foreach (var r in rows)
        {
            r.TrendKeyword = null;
            r.TrendTraffic = null;
        }
        if (trends.Count == 0) return;

        var prepared = trends
            .Select(t => (t, tokens: Tokens(t.Keyword, foldDiacritics: true)))
            .Where(x => x.tokens.Count > 0)
            .ToList();

        foreach (var r in rows)
        {
            var titleTokens = Tokens(r.Title, foldDiacritics: true);
            TrendTerm? best = null;
            foreach (var (t, tokens) in prepared)
            {
                var hit = t.NewsUrls.Contains(r.Url) || tokens.All(titleTokens.Contains);
                if (hit && (best == null || t.Traffic > best.Traffic)) best = t;
            }
            if (best != null)
            {
                r.TrendKeyword = best.Keyword.Length > 200 ? best.Keyword[..200] : best.Keyword;
                r.TrendTraffic = best.Traffic;
            }
        }

        // A story inherits the strongest trend match of any outlet covering it
        foreach (var cluster in rows.Where(r => r.ClusterId != null).GroupBy(r => r.ClusterId))
        {
            var top = cluster.Where(r => r.TrendTraffic != null).OrderByDescending(r => r.TrendTraffic).FirstOrDefault();
            if (top == null) continue;
            foreach (var r in cluster.Where(r => r.TrendTraffic == null))
            {
                r.TrendKeyword = top.TrendKeyword;
                r.TrendTraffic = top.TrendTraffic;
            }
        }
    }

    /* ───────────────────────────── Clustering ───────────────────────────── */

    private static HashSet<string> Tokens(string text, bool foldDiacritics = false)
    {
        var source = foldDiacritics ? FoldDiacritics(text) : text;
        return WordRegex.Matches(source.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(t => t.Length >= 2 && !StopWords.Contains(t) && !StopWords.Contains(FoldDiacritics(t)))
            .ToHashSet();
    }

    private static string FoldDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D');
    }

    /// <summary>Groups stories reported by several outlets (same language, strongly overlapping titles).</summary>
    private static void Cluster(List<TrendingArticle> rows, List<FeedSource> feeds)
    {
        var n = rows.Count;
        var tokens = rows.Select(r => Tokens(r.Title)).ToList();
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }

        // Inverted index so we only compare titles that share at least one word
        var index = new Dictionary<string, List<int>>();
        for (var i = 0; i < n; i++)
            foreach (var t in tokens[i])
                (index.TryGetValue(t, out var l) ? l : index[t] = new List<int>()).Add(i);

        for (var i = 0; i < n; i++)
        {
            var shared = new Dictionary<int, int>();
            foreach (var t in tokens[i])
            {
                var posting = index[t];
                if (posting.Count > 150) continue; // too common to be meaningful
                foreach (var j in posting)
                    if (j > i) shared[j] = shared.GetValueOrDefault(j) + 1;
            }
            foreach (var (j, count) in shared)
            {
                if (rows[i].Language != rows[j].Language) continue;
                var min = Math.Min(tokens[i].Count, tokens[j].Count);
                if (count >= 3 && min > 0 && (double)count / min >= 0.5)
                {
                    parent[Find(i)] = Find(j);
                }
            }
        }

        var priority = feeds.Select(f => f.Key).Distinct().Select((k, idx) => (k, idx)).ToDictionary(x => x.k, x => x.idx);
        var unreadable = UnreadableKeys(feeds);
        foreach (var group in Enumerable.Range(0, n).GroupBy(Find))
        {
            var members = group.Select(i => rows[i]).ToList();
            // The card must link to an article we can actually turn into a video
            var rep = members
                .OrderBy(m => unreadable.Contains(m.SourceKey))
                .ThenByDescending(m => m.IsFeatured)
                .ThenBy(m => priority.GetValueOrDefault(m.SourceKey, 999))
                .ThenByDescending(m => m.PublishedAtUtc)
                .First();
            var outlets = members.Select(m => m.SourceName).Distinct().ToList();
            var related = outlets.Where(o => o != rep.SourceName).ToList();
            foreach (var m in members)
            {
                m.ClusterId = rep.Id;
                m.CoverageCount = outlets.Count;
                m.RelatedSources = related.Count > 0 ? string.Join(", ", related) : null;
            }
        }
    }

    /* ───────────────────────────── Query ───────────────────────────── */

    public async Task<TrendingOverviewDto> GetTrendingAsync(TrendingQuery query, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var hours = Math.Clamp(query.Hours, 1, KeepHours);
        var since = now.AddHours(-hours);

        // Unreadable outlets (blocked in VN / anti-bot) only boost coverage; their own articles aren't listed
        var unreadable = UnreadableKeys(GetFeeds());
        var rows = (await _context.TrendingArticles.AsNoTracking()
                .Where(a => a.PublishedAtUtc >= since)
                .ToListAsync(cancellationToken))
            .Where(a => !unreadable.Contains(a.SourceKey) && !NonArticleUrl.IsMatch(a.Url))
            .ToList();

        var sources = rows
            .GroupBy(r => new { r.SourceKey, r.SourceName, r.Region, r.Language })
            .Select(g => new TrendingSourceDto
            {
                Key = g.Key.SourceKey, Name = g.Key.SourceName, Region = g.Key.Region, Language = g.Key.Language, ItemCount = g.Count()
            })
            .OrderBy(s => s.Region == "VN" ? 0 : 1).ThenByDescending(s => s.ItemCount)
            .ToList();

        IEnumerable<TrendingArticle> filtered = rows;
        if (query.Region is "VN" or "World") filtered = filtered.Where(r => r.Region == query.Region);
        if (!string.IsNullOrWhiteSpace(query.Source)) filtered = filtered.Where(r => r.SourceKey == query.Source);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var q = FoldDiacritics(query.Search.Trim()).ToLowerInvariant();
            filtered = filtered.Where(r => FoldDiacritics(r.Title).ToLowerInvariant().Contains(q)
                                           || (r.Summary != null && FoldDiacritics(r.Summary).ToLowerInvariant().Contains(q)));
        }

        // One card per story: when filtering by a single source show its own article, otherwise the
        // cluster representative (or the first match if the representative was filtered out).
        var items = (string.IsNullOrWhiteSpace(query.Source)
                ? filtered.GroupBy(r => r.ClusterId ?? r.Id)
                    .Select(g => g.FirstOrDefault(r => r.Id == g.Key) ?? g.OrderByDescending(r => r.PublishedAtUtc).First())
                : filtered)
            .Select(r => ToDto(r, now))
            .OrderByDescending(d => d.HotScore)
            .ThenByDescending(d => d.PublishedAtUtc)
            .Take(Math.Clamp(query.Limit, 1, 200))
            .ToList();

        return new TrendingOverviewDto
        {
            Items = items,
            Sources = sources,
            LastRefresh = await ReadSettingAsync<TrendingRefreshResultDto>(LastRefreshKey, cancellationToken),
            TrendKeywords = await ReadSettingAsync<List<string>>(KeywordsKey, cancellationToken) ?? new List<string>()
        };
    }

    private static TrendingArticleDto ToDto(TrendingArticle r, DateTime now)
    {
        var ageHours = Math.Max(0, (now - r.PublishedAtUtc).TotalHours);
        var signal = 1.0
                     + 1.6 * Math.Max(0, r.CoverageCount - 1)
                     + (r.TrendTraffic.HasValue ? 1.0 + Math.Log10(Math.Max(10, r.TrendTraffic.Value)) : 0)
                     + (r.IsFeatured ? 1.5 : 0);
        var raw = signal * Math.Pow(0.5, ageHours / HalfLifeHours);
        var score = Math.Round(100 * (1 - Math.Exp(-raw / 3.0)), 1);

        var reasons = new List<string>();
        if (r.CoverageCount > 1) reasons.Add($"{r.CoverageCount} báo cùng đưa tin");
        if (!string.IsNullOrEmpty(r.TrendKeyword))
            reasons.Add($"Đang được tìm nhiều trên Google: “{r.TrendKeyword}”" + (r.TrendTraffic > 0 ? $" ({FormatTraffic(r.TrendTraffic.Value)} lượt)" : ""));
        if (r.IsFeatured) reasons.Add($"Tin nổi bật trên {r.SourceName}");
        if (ageHours < 2) reasons.Add("Vừa đăng");

        return new TrendingArticleDto
        {
            Id = r.Id,
            Url = r.Url,
            Title = r.Title,
            Summary = r.Summary,
            ImageUrl = r.ImageUrl,
            SourceKey = r.SourceKey,
            SourceName = r.SourceName,
            Region = r.Region,
            Language = r.Language,
            PublishedAtUtc = DateTime.SpecifyKind(r.PublishedAtUtc, DateTimeKind.Utc),
            IsFeatured = r.IsFeatured,
            CoverageCount = r.CoverageCount,
            RelatedSources = string.IsNullOrWhiteSpace(r.RelatedSources)
                ? new List<string>()
                : r.RelatedSources.Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList(),
            TrendKeyword = r.TrendKeyword,
            TrendTraffic = r.TrendTraffic,
            HotScore = score,
            Reasons = reasons
        };
    }

    private static string FormatTraffic(int n) =>
        n >= 1_000_000 ? $"{n / 1_000_000}M+" : n >= 1_000 ? $"{n / 1_000}K+" : $"{n}+";

    /* ───────────────────────────── Settings helpers ───────────────────────────── */

    private async Task SaveSettingAsync(string key, string value, CancellationToken ct)
    {
        var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == key, ct);
        if (setting == null)
        {
            _context.SystemSettings.Add(new SystemSetting { SettingKey = key, SettingValue = value, Group = "Trending" });
        }
        else
        {
            setting.SettingValue = value;
            setting.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private async Task<T?> ReadSettingAsync<T>(string key, CancellationToken ct)
    {
        var setting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.SettingKey == key, ct);
        if (setting == null || string.IsNullOrWhiteSpace(setting.SettingValue)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(setting.SettingValue);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
