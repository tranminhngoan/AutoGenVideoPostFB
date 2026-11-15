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
        }

        // 4. Extract Images strictly from within article body or figure tags
        ExtractBodyImages(doc, uri, result);

        return result;
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

        // Fallback: If still no article images found, use verified og:image from official metadata
        if (result.Images.Count == 0)
        {
            var ogImage = ExtractMeta(doc, "og:image");
            if (!string.IsNullOrWhiteSpace(ogImage) && !IsAdOrIcon(ogImage))
            {
                var fullUrl = ResolveUrl(ogImage, uri);
                result.Images.Add(new ExtractedImageRaw
                {
                    Url = fullUrl,
                    Caption = result.Title,
                    AltText = result.Title,
                    Order = 1
                });
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

    protected static string ResolveImageUrl(HtmlNode imgNode, Uri baseUri)
    {
        var src = imgNode.GetAttributeValue("data-src", (string?)null);
        if (string.IsNullOrWhiteSpace(src)) src = imgNode.GetAttributeValue("data-original", (string?)null);
        if (string.IsNullOrWhiteSpace(src)) src = imgNode.GetAttributeValue("src", (string?)null);

        return ResolveUrl(src, baseUri);
    }

    protected static string ResolveUrl(string? url, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        url = url.Trim();

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
