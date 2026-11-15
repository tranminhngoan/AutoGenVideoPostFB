using HtmlAgilityPack;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Infrastructure.Extractors;

public interface IWebsiteAdapter
{
    bool CanHandle(Uri uri);
    ExtractedArticleRaw Extract(HtmlDocument doc, Uri uri);
}
