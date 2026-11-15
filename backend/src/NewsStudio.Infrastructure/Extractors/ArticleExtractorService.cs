using System.Security.Cryptography;
using System.Text;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Interfaces;
using NewsStudio.Infrastructure.Extractors.Adapters;

namespace NewsStudio.Infrastructure.Extractors;

public class ArticleExtractorService : IArticleExtractorService
{
    private readonly HttpClient _httpClient;
    private readonly IEnumerable<IWebsiteAdapter> _adapters;
    private readonly ILogger<ArticleExtractorService> _logger;

    public ArticleExtractorService(
        HttpClient httpClient,
        ILogger<ArticleExtractorService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Register adapter chain in precedence order
        _adapters = new List<IWebsiteAdapter>
        {
            new VnExpressAdapter(),
            new TuoiTreAdapter(),
            new ThanhNienAdapter(),
            new DanTriAdapter(),
            new GenericArticleAdapter() // Fallback
        };
    }

    public async Task<ExtractedArticleRaw> ExtractFromUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"Invalid URL format: '{url}'", nameof(url));
        }

        _logger.LogInformation("Fetching article content from URL: {Url}", url);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 AI-News-Studio/1.0");
        request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.Add("Accept-Language", "vi-VN,vi;q=0.9,en-US;q=0.8,en;q=0.7");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // e.g. BBC is blocked in Vietnam: the TLS handshake never completes
            throw new InvalidOperationException(
                $"Không mở được trang {uri.Host} — trang có thể bị chặn tại Việt Nam hoặc mạng không truy cập được ({ex.Message}). Hãy chọn bài từ báo khác.", ex);
        }

        using var _ = response;
        if (!response.IsSuccessStatusCode)
        {
            var code = (int)response.StatusCode;
            throw new InvalidOperationException(code is 401 or 402 or 403 or 429
                ? $"{uri.Host} từ chối cho máy chủ đọc bài (HTTP {code}) — trang chặn truy cập tự động hoặc cần đăng nhập/trả phí. Hãy chọn bài từ báo khác."
                : $"Không tải được bài báo từ {uri.Host} (HTTP {code}).");
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return await ExtractFromHtmlAsync(html, url, cancellationToken);
    }

    public Task<ExtractedArticleRaw> ExtractFromHtmlAsync(string html, string sourceUrl, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(sourceUrl);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var adapter = _adapters.FirstOrDefault(a => a.CanHandle(uri)) ?? new GenericArticleAdapter();
        _logger.LogInformation("Using extractor adapter {AdapterName} for {Host}", adapter.GetType().Name, uri.Host);

        var extracted = adapter.Extract(doc, uri);

        // Deduplicate images and verify SHA256 hashes
        var deduplicatedImages = new List<ExtractedImageRaw>();
        var seenHashes = new HashSet<string>();

        foreach (var img in extracted.Images)
        {
            var urlHash = ComputeSha256(img.Url);
            if (seenHashes.Add(urlHash))
            {
                img.Order = deduplicatedImages.Count + 1;
                deduplicatedImages.Add(img);
            }
        }

        extracted.Images = deduplicatedImages;

        _logger.LogInformation("Extracted article '{Title}' with {Count} images from {Url}",
            extracted.Title, extracted.Images.Count, sourceUrl);

        return Task.FromResult(extracted);
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
