using HtmlAgilityPack;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Infrastructure.Extractors.Adapters;

public class VnExpressAdapter : GenericArticleAdapter
{
    public override bool CanHandle(Uri uri)
    {
        return uri.Host.Contains("vnexpress.net", StringComparison.OrdinalIgnoreCase);
    }

    public override ExtractedArticleRaw Extract(HtmlDocument doc, Uri uri)
    {
        var raw = base.Extract(doc, uri);
        raw.Publisher = "VnExpress";

        // Specific VnExpress title selector
        var titleNode = doc.DocumentNode.SelectSingleNode("//h1[contains(@class, 'title-detail')]");
        if (titleNode != null && !string.IsNullOrWhiteSpace(titleNode.InnerText))
        {
            raw.Title = CleanText(titleNode.InnerText);
        }

        // Specific description selector
        var descNode = doc.DocumentNode.SelectSingleNode("//p[contains(@class, 'description')]");
        if (descNode != null && !string.IsNullOrWhiteSpace(descNode.InnerText))
        {
            raw.Description = CleanText(descNode.InnerText);
        }

        // Specific author selector
        var authorNode = doc.DocumentNode.SelectSingleNode("//p[contains(@class, 'author_mail')]//strong")
                         ?? doc.DocumentNode.SelectSingleNode("//article[contains(@class, 'fck_detail')]//p[contains(@class, 'Normal')][last()]//strong")
                         ?? doc.DocumentNode.SelectSingleNode("//*[contains(@class, 'author')]")
                         ?? doc.DocumentNode.SelectSingleNode("//meta[@name='author']");
        if (authorNode != null)
        {
            var authorText = authorNode.Name == "meta" 
                ? authorNode.GetAttributeValue("content", "") 
                : authorNode.InnerText;
            raw.Author = CleanText(authorText);
        }

        // Specific body paragraphs (p.Normal within fck_detail)
        var bodyNodes = doc.DocumentNode.SelectNodes("//article[contains(@class, 'fck_detail')]//p[contains(@class, 'Normal')]");
        if (bodyNodes != null && bodyNodes.Count > 0)
        {
            var paragraphs = bodyNodes
                .Select(p => CleanText(p.InnerText))
                .Where(t => t.Length > 20 && !t.StartsWith("Ảnh:") && !t.StartsWith("Video:"))
                .ToList();

            if (paragraphs.Count > 0)
            {
                raw.ContentBody = string.Join("\n\n", paragraphs);
            }
        }

        // Specific figures with class tplCaption
        var vnExpressFigures = doc.DocumentNode.SelectNodes("//article[contains(@class, 'fck_detail')]//figure[contains(@class, 'tplCaption')]");
        if (vnExpressFigures != null && vnExpressFigures.Count > 0)
        {
            raw.Images.Clear();
            var addedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int order = 1;

            foreach (var fig in vnExpressFigures)
            {
                var img = fig.SelectSingleNode(".//img");
                if (img == null) continue;

                var imgUrl = ResolveImageUrl(img, uri);
                if (string.IsNullOrWhiteSpace(imgUrl) || IsAdOrIcon(imgUrl) || !addedUrls.Add(imgUrl)) continue;

                var captionNode = fig.SelectSingleNode(".//figcaption") ?? fig.SelectSingleNode(".//p[contains(@class, 'Image')]");
                var caption = CleanText(captionNode?.InnerText ?? img.GetAttributeValue("alt", ""));

                raw.Images.Add(new ExtractedImageRaw
                {
                    Url = imgUrl,
                    Caption = string.IsNullOrWhiteSpace(caption) ? raw.Title : caption,
                    AltText = CleanText(img.GetAttributeValue("alt", "")),
                    Order = order++
                });
            }
        }

        return raw;
    }
}
