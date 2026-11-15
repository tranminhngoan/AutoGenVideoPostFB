using HtmlAgilityPack;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Infrastructure.Extractors.Adapters;

public class ThanhNienAdapter : GenericArticleAdapter
{
    public override bool CanHandle(Uri uri)
    {
        return uri.Host.Contains("thanhnien.vn", StringComparison.OrdinalIgnoreCase);
    }

    public override ExtractedArticleRaw Extract(HtmlDocument doc, Uri uri)
    {
        var raw = base.Extract(doc, uri);
        raw.Publisher = "Thanh Niên";

        var titleNode = doc.DocumentNode.SelectSingleNode("//h1[contains(@class, 'detail-title')]");
        if (titleNode != null && !string.IsNullOrWhiteSpace(titleNode.InnerText))
        {
            raw.Title = CleanText(titleNode.InnerText);
        }

        var descNode = doc.DocumentNode.SelectSingleNode("//h2[contains(@class, 'detail-sapo')]");
        if (descNode != null && !string.IsNullOrWhiteSpace(descNode.InnerText))
        {
            raw.Description = CleanText(descNode.InnerText);
        }

        var bodyNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'detail-cmain')]//p");
        if (bodyNodes != null && bodyNodes.Count > 0)
        {
            var paragraphs = bodyNodes
                .Select(p => CleanText(p.InnerText))
                .Where(t => t.Length > 20)
                .ToList();

            if (paragraphs.Count > 0)
            {
                raw.ContentBody = string.Join("\n\n", paragraphs);
            }
        }

        return raw;
    }
}

public class DanTriAdapter : GenericArticleAdapter
{
    public override bool CanHandle(Uri uri)
    {
        return uri.Host.Contains("dantri.com.vn", StringComparison.OrdinalIgnoreCase);
    }

    public override ExtractedArticleRaw Extract(HtmlDocument doc, Uri uri)
    {
        var raw = base.Extract(doc, uri);
        raw.Publisher = "Dân Trí";

        var titleNode = doc.DocumentNode.SelectSingleNode("//h1[contains(@class, 'title-page')]");
        if (titleNode != null && !string.IsNullOrWhiteSpace(titleNode.InnerText))
        {
            raw.Title = CleanText(titleNode.InnerText);
        }

        var descNode = doc.DocumentNode.SelectSingleNode("//h2[contains(@class, 'singular-sapo')]");
        if (descNode != null && !string.IsNullOrWhiteSpace(descNode.InnerText))
        {
            raw.Description = CleanText(descNode.InnerText);
        }

        return raw;
    }
}
