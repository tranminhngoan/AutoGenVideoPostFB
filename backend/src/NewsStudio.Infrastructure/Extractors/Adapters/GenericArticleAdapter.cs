using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Infrastructure.Extractors.Adapters;

public class GenericArticleAdapter : IWebsiteAdapter
{
    public virtual bool CanHandle(Uri uri) => true; // Fallback handler for all sites

    public virtual ExtractedArticleRaw Extract(HtmlDocument doc, Uri uri)
    {
        var result = new ExtractedArticleRaw
        {
            Publisher = ExtractMeta(doc, "og:site_name") ?? uri.Host.Replace("www.", ""),
            CanonicalUrl = ExtractMeta(doc, "og:url") ?? ExtractLinkRel(doc, "canonical") ?? uri.ToString()
        };

        // 1. Try JSON-LD first for structured journalism data
        var jsonLdNodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (jsonLdNodes != null)
        {
            foreach (var node in jsonLdNodes)
            {
                if (TryParseJsonLd(node.InnerText, result))
                {
                    break;
                }
            }
        }

        // 2. OpenGraph Fallbacks if JSON-LD missing
        if (string.IsNullOrWhiteSpace(result.Title))
        {
            result.Title = CleanText(ExtractMeta(doc, "og:title") 
                ?? doc.DocumentNode.SelectSingleNode("//h1")?.InnerText 
                ?? doc.DocumentNode.SelectSingleNode("//title")?.InnerText 
                ?? "");
        }

        if (string.IsNullOrWhiteSpace(result.Description))
        {
            result.Description = CleanText(ExtractMeta(doc, "og:description") ?? ExtractMetaName(doc, "description") ?? "");
        }

        if (string.IsNullOrWhiteSpace(result.Author))
        {
            result.Author = CleanText(ExtractMeta(doc, "article:author") ?? ExtractMetaName(doc, "author") ?? "");
        }

        if (!result.PublishedAtUtc.HasValue)
        {
            var pubDateStr = ExtractMeta(doc, "article:published_time") ?? ExtractMetaName(doc, "pubdate");
            if (DateTime.TryParse(pubDateStr, out var parsedDate))
            {
                result.PublishedAtUtc = parsedDate.ToUniversalTime();
            }
        }

        // 3. Extract Main Article Body from semantic HTML
        if (string.IsNullOrWhiteSpace(result.ContentBody))
        {
            var articleNode = doc.DocumentNode.SelectSingleNode("//article")
                              ?? doc.DocumentNode.SelectSingleNode("//main")
                              ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'content') or contains(@class, 'detail')]");

            if (articleNode != null)
            {
                var paragraphs = articleNode.SelectNodes(".//p");
                if (paragraphs != null)
                {
                    var bodyTexts = paragraphs
                        .Select(p => CleanText(p.InnerText))
                        .Where(t => t.Length > 25 && !t.Contains("Bản quyền") && !t.Contains("Theo dõi"))
                        .ToList();

                    result.ContentBody = string.Join("\n\n", bodyTexts);
                }
            }

            // The first <article>/<main> is sometimes a sidebar teaser (e.g. CafeF): when it gave almost
            // nothing, use the content-like container that holds the most paragraph text.
            if (CountWords(result.ContentBody) < 80)
            {
                var best = BestContentParagraphs(doc);
                if (CountWords(best) > CountWords(result.ContentBody)) result.ContentBody = best;
            }
        }

        // 4. Extract Images strictly from within article body or figure tags
        ExtractBodyImages(doc, uri, result);

        return result;
    }

    private static int CountWords(string? text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static List<string> ParagraphTexts(HtmlNode node) =>
        (node.SelectNodes(".//p")?.ToList() ?? new List<HtmlNode>())
            .Where(p => !p.Ancestors("p").Any()) // nested <p>: the outer one already holds its text
            .Select(p => CleanText(p.InnerText))
            .Where(t => t.Length > 25 && !t.Contains("Bản quyền") && !t.Contains("Theo dõi"))
            .Distinct()
            .ToList();

    /// <summary>
    /// Paragraphs of the container with the most article text. Among containers holding nearly as much
    /// (≥ 80%), the innermost wins, so a page-wide wrapper doesn't drag in sidebars and footers.
    /// </summary>
    private static string BestContentParagraphs(HtmlDocument doc)
    {
        var candidates = doc.DocumentNode.SelectNodes(
            "//article | //main | //*[@itemprop='articleBody'] | //div[contains(@class, 'content') or contains(@class, 'detail') or contains(@class, 'body')]");
        if (candidates == null) return string.Empty;

        var scored = candidates
            .Select(n => (Node: n, Texts: ParagraphTexts(n)))
            .Select(x => (x.Node, x.Texts, Length: x.Texts.Sum(t => t.Length)))
            .Where(x => x.Length > 0)
            .ToList();
        if (scored.Count == 0) return string.Empty;

        var max = scored.Max(x => x.Length);
        var pick = scored
            .Where(x => x.Length >= max * 0.8)
            .OrderBy(x => x.Node.Descendants().Count())
            .First();
        return string.Join("\n\n", pick.Texts);
    }

    protected void ExtractBodyImages(HtmlDocument doc, Uri uri, ExtractedArticleRaw result)
    {
        var addedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Find figures within article or document
        var figureNodes = doc.DocumentNode.SelectNodes("//figure");
        int order = 1;

        if (figureNodes != null)
        {
            foreach (var fig in figureNodes)
            {
                var imgNode = fig.SelectSingleNode(".//img");
                if (imgNode == null) continue;

                var imgUrl = ResolveImageUrl(imgNode, uri);
                if (string.IsNullOrWhiteSpace(imgUrl) || IsAdOrIcon(imgUrl) || !addedUrls.Add(imgUrl))
                {
                    continue;
                }

                var captionNode = fig.SelectSingleNode(".//figcaption") ?? fig.SelectSingleNode(".//p[contains(@class, 'caption')]");
                var caption = CleanText(captionNode?.InnerText ?? imgNode.GetAttributeValue("alt", ""));

                result.Images.Add(new ExtractedImageRaw
                {
                    Url = imgUrl,
                    Caption = string.IsNullOrWhiteSpace(caption) ? result.Title : caption,
                    AltText = CleanText(imgNode.GetAttributeValue("alt", "")),
                    Order = order++
                });
            }
        }

        // If no figures found, check standard content images (excluding sidebars, headers, footers)
        if (result.Images.Count == 0)
        {
            var contentImgs = doc.DocumentNode.SelectNodes("//article//img | //div[contains(@class, 'detail') or contains(@class, 'content')]//img");
            if (contentImgs != null)
            {
                foreach (var img in contentImgs)
                {
                    var imgUrl = ResolveImageUrl(img, uri);
                    if (string.IsNullOrWhiteSpace(imgUrl) || IsAdOrIcon(imgUrl) || !addedUrls.Add(imgUrl))
                    {
                        continue;
                    }

                    var alt = CleanText(img.GetAttributeValue("alt", ""));
                    result.Images.Add(new ExtractedImageRaw
                    {
                        Url = imgUrl,
                        Caption = !string.IsNullOrWhiteSpace(alt) ? alt : result.Title,
                        AltText = alt,
                        Order = order++
                    });
                }
            }
        }

        // The og:image is the article's own lead photo (large, editor-chosen). Use it when the body
        // gave us fewer than two usable images.
        if (result.Images.Count < 2)
        {
            var ogLead = ExtractMeta(doc, "og:image");
            if (!string.IsNullOrWhiteSpace(ogLead) && !IsAdOrIcon(ogLead))
            {
                var leadUrl = ResolveUrl(ogLead, uri);
                var leadPath = leadUrl.Split('?')[0];
                if (!result.Images.Any(i => i.Url.Split('?')[0] == leadPath) && addedUrls.Add(leadUrl))
                {
                    foreach (var img in result.Images) img.Order++;
                    result.Images.Insert(0, new ExtractedImageRaw { Url = leadUrl, Caption = result.Title, AltText = result.Title, Order = 1 });
                }
            }
        }
    }

    protected static string? ExtractMeta(HtmlDocument doc, string propertyName)
    {
        return doc.DocumentNode.SelectSingleNode($"//meta[@property='{propertyName}']")?.GetAttributeValue("content", null)
            ?? doc.DocumentNode.SelectSingleNode($"//meta[@name='{propertyName}']")?.GetAttributeValue("content", null);
    }

    protected static string? ExtractMetaName(HtmlDocument doc, string name)
    {
        return doc.DocumentNode.SelectSingleNode($"//meta[@name='{name}']")?.GetAttributeValue("content", null);
    }

    protected static string? ExtractLinkRel(HtmlDocument doc, string rel)
    {
        return doc.DocumentNode.SelectSingleNode($"//link[@rel='{rel}']")?.GetAttributeValue("href", null);
    }

    /// <summary>
    /// Best URL for an &lt;img&gt;: the widest candidate from srcset / data-srcset / a parent
    /// &lt;picture&gt;'s &lt;source&gt;, otherwise data-src / data-original / src.
    /// International sites (Guardian, NYT, CNBC…) put the real, high-resolution image in srcset.
    /// </summary>
    protected static string ResolveImageUrl(HtmlNode imgNode, Uri baseUri)
    {
        var srcsets = new List<string?>
        {
            imgNode.GetAttributeValue("data-srcset", (string?)null),
            imgNode.GetAttributeValue("srcset", (string?)null)
        };
        if (imgNode.ParentNode?.Name == "picture")
        {
            srcsets.AddRange(imgNode.ParentNode.SelectNodes("./source")?
                .Select(s => s.GetAttributeValue("srcset", (string?)null) ?? s.GetAttributeValue("data-srcset", (string?)null))
                ?? Enumerable.Empty<string?>());
        }

        var best = srcsets
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .SelectMany(s => ParseSrcset(s!))
            .Where(c => c.Width <= 2400)            // no need for print-size originals
            .OrderByDescending(c => c.Width)
            .Select(c => c.Url)
            .FirstOrDefault();

        var src = best
                  ?? imgNode.GetAttributeValue("data-src", (string?)null)
                  ?? imgNode.GetAttributeValue("data-original", (string?)null)
                  ?? imgNode.GetAttributeValue("src", (string?)null);

        return ResolveUrl(src, baseUri);
    }

    private static IEnumerable<(string Url, int Width)> ParseSrcset(string srcset)
    {
        // "url 465w, url 930w" (URLs may themselves contain commas only when encoded)
        foreach (var part in WebUtility.HtmlDecode(srcset).Split(", ", StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (bits.Length == 0) continue;
            var width = 0;
            if (bits.Length > 1)
            {
                var d = bits[^1];
                if (d.EndsWith('w')) int.TryParse(d.TrimEnd('w'), out width);
                else if (d.EndsWith('x') && double.TryParse(d.TrimEnd('x'), System.Globalization.NumberStyles.Any,
                             System.Globalization.CultureInfo.InvariantCulture, out var x)) width = (int)(x * 600);
            }
            yield return (bits[0], width);
        }
    }

    protected static string ResolveUrl(string? url, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        // HTML attributes keep entities encoded ("&amp;"). Signed image URLs (e.g. the Guardian's
        // i.guim.co.uk) are rejected with 401 unless the query string is decoded exactly.
        url = WebUtility.HtmlDecode(url.Trim());

        if (url.StartsWith("//")) return $"{baseUri.Scheme}:{url}";
        if (Uri.TryCreate(url, UriKind.Absolute, out var absUri)) return absUri.ToString();
        if (Uri.TryCreate(baseUri, url, out var combinedUri)) return combinedUri.ToString();

        return url;
    }

    protected static bool IsAdOrIcon(string url)
    {
        var lower = url.ToLowerInvariant();
        return lower.Contains("/ads/") ||
               lower.Contains("advert") ||
               lower.Contains("banner") ||
               lower.Contains("logo") ||
               lower.Contains("icon") ||
               lower.Contains("avatar") ||
               // Byline / author photos are not news images (e.g. NYT ".../reader-center/author-…")
               lower.Contains("/author") ||
               lower.Contains("author-") ||
               lower.Contains("author_") ||
               lower.Contains("byline") ||
               lower.Contains("headshot") ||
               lower.Contains("/profile") ||
               lower.Contains("gravatar") ||
               lower.Contains("placeholder") ||
               lower.Contains("spacer") ||
               lower.Contains("1x1") ||
               lower.Contains("sprite") ||
               lower.StartsWith("data:") ||
               lower.Contains("facebook.svg") ||
               lower.Contains("zalo.svg") ||
               lower.EndsWith(".svg") ||
               lower.EndsWith(".gif");
    }

    protected static string CleanText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var decoded = WebUtility.HtmlDecode(input);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private static bool TryParseJsonLd(string json, ExtractedArticleRaw result)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                {
                    if (ExtractFromJsonElement(el, result)) return true;
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in graph.EnumerateArray())
                    {
                        if (ExtractFromJsonElement(el, result)) return true;
                    }
                }
                else
                {
                    return ExtractFromJsonElement(root, result);
                }
            }
        }
        catch
        {
            // Ignore non-parseable JSON-LD
        }

        return false;
    }

    private static bool ExtractFromJsonElement(JsonElement el, ExtractedArticleRaw result)
    {
        if (el.TryGetProperty("@type", out var typeEl))
        {
            var typeStr = typeEl.GetString() ?? "";
            if (typeStr.Contains("NewsArticle") || typeStr.Contains("Article") || typeStr.Contains("BlogPosting"))
            {
                if (el.TryGetProperty("headline", out var headline))
                    result.Title = CleanText(headline.GetString());

                if (el.TryGetProperty("description", out var desc))
                    result.Description = CleanText(desc.GetString());

                if (el.TryGetProperty("articleBody", out var body))
                    result.ContentBody = CleanText(body.GetString());

                if (el.TryGetProperty("datePublished", out var date) && DateTime.TryParse(date.GetString(), out var pubDate))
                    result.PublishedAtUtc = pubDate.ToUniversalTime();

                if (el.TryGetProperty("author", out var author))
                {
                    if (author.ValueKind == JsonValueKind.Object && author.TryGetProperty("name", out var authorName))
                        result.Author = CleanText(authorName.GetString());
                    else if (author.ValueKind == JsonValueKind.String)
                        result.Author = CleanText(author.GetString());
                }

                if (el.TryGetProperty("publisher", out var pub) && pub.TryGetProperty("name", out var pubName))
                    result.Publisher = CleanText(pubName.GetString());

                return true;
            }
        }
        return false;
    }
}
