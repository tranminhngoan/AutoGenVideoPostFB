using HtmlAgilityPack;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Infrastructure.Extractors.Adapters;

public class TuoiTreAdapter : GenericArticleAdapter
{
    public override bool CanHandle(Uri uri)
    {
        return uri.Host.Contains("tuoitre.vn", StringComparison.OrdinalIgnoreCase);
    }

    public override ExtractedArticleRaw Extract(HtmlDocument doc, Uri uri)
    {
        var raw = base.Extract(doc, uri);
        raw.Publisher = "Tuổi Trẻ Online";

        var titleNode = doc.DocumentNode.SelectSingleNode("//h1[contains(@class, 'article-title')]");
        if (titleNode != null && !string.IsNullOrWhiteSpace(titleNode.InnerText))
        {
            raw.Title = CleanText(titleNode.InnerText);
        }

        var descNode = doc.DocumentNode.SelectSingleNode("//h2[contains(@class, 'article-sapo')]");
        if (descNode != null && !string.IsNullOrWhiteSpace(descNode.InnerText))
        {
            raw.Description = CleanText(descNode.InnerText);
        }

        var bodyNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'detail-content')]//p");
        if (bodyNodes != null && bodyNodes.Count > 0)
        {
            var paragraphs = bodyNodes
                .Select(p => CleanText(p.InnerText))
                .Where(t => t.Length > 20 && !t.Contains("TTO") && !t.Contains("Tuổi Trẻ"))
                .ToList();

            if (paragraphs.Count > 0)
            {
                raw.ContentBody = string.Join("\n\n", paragraphs);
            }
        }

        // TuoiTre photo block
        var photoBlocks = doc.DocumentNode.SelectNodes("//div[contains(@class, 'VCSortable')] | //div[contains(@class, 'PhotoCMS')] | //figure");
        if (photoBlocks != null && photoBlocks.Count > 0)
        {
            raw.Images.Clear();
            var addedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int order = 1;

            foreach (var block in photoBlocks)
            {
                var img = block.SelectSingleNode(".//img");
                if (img == null) continue;

                var imgUrl = ResolveImageUrl(img, uri);
                if (string.IsNullOrWhiteSpace(imgUrl) || IsAdOrIcon(imgUrl) || !addedUrls.Add(imgUrl)) continue;

                var captionNode = block.SelectSingleNode(".//div[contains(@class, 'PhotoCMS_Caption')]")
                                  ?? block.SelectSingleNode(".//figcaption")
                                  ?? block.SelectSingleNode(".//p[contains(@class, 'caption')]");
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
