using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Application.Interfaces;

public class ExtractedArticleRaw
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ContentBody { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string Publisher { get; set; } = string.Empty;
    public string CanonicalUrl { get; set; } = string.Empty;
    public DateTime? PublishedAtUtc { get; set; }
    public List<ExtractedImageRaw> Images { get; set; } = new();
}

public class ExtractedImageRaw
{
    public string Url { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public string? AltText { get; set; }
    public int Order { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
}

public interface IArticleExtractorService
{
    Task<ExtractedArticleRaw> ExtractFromUrlAsync(string url, CancellationToken cancellationToken = default);
    Task<ExtractedArticleRaw> ExtractFromHtmlAsync(string html, string sourceUrl, CancellationToken cancellationToken = default);
}

public interface IArticleService
{
    Task<ArticleResponseDto> ExtractAndSaveArticleAsync(string url, CancellationToken cancellationToken = default);
    Task<IEnumerable<ArticleResponseDto>> GetArticlesAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<ArticleResponseDto?> GetArticleByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ArticleImageResponseDto?> UpdateArticleImageAsync(Guid articleId, Guid imageId, UpdateArticleImageDto dto, CancellationToken cancellationToken = default);
}
