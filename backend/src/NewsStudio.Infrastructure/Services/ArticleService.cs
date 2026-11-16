using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Infrastructure.Services;

public class ArticleService : IArticleService
{
    private readonly IApplicationDbContext _context;
    private readonly IArticleExtractorService _extractorService;
    private readonly IImageClassifierService _classifierService;
    private readonly ILogger<ArticleService> _logger;

    public ArticleService(
        IApplicationDbContext context,
        IArticleExtractorService extractorService,
        IImageClassifierService classifierService,
        ILogger<ArticleService> logger)
    {
        _context = context;
        _extractorService = extractorService;
        _classifierService = classifierService;
        _logger = logger;
    }

    public async Task<ArticleResponseDto> ExtractAndSaveArticleAsync(string url, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing article extraction request for URL: {Url}", url);

        // Check if article with this URL already extracted
        var existing = await _context.Articles
            .Include(a => a.Images)
            .FirstOrDefaultAsync(a => a.Url == url, cancellationToken);

        if (existing != null)
        {
            _logger.LogInformation("Article for URL {Url} was already extracted (ID: {Id}). Returning existing record.", url, existing.Id);
            return MapToDto(existing);
        }

        var raw = await _extractorService.ExtractFromUrlAsync(url, cancellationToken);

        var article = new Article
        {
            Title = string.IsNullOrWhiteSpace(raw.Title) ? "Không có tiêu đề" : raw.Title,
            NormalizedTitle = raw.Title.ToLowerInvariant().Trim(),
            Url = url,
            CanonicalUrl = string.IsNullOrWhiteSpace(raw.CanonicalUrl) ? url : raw.CanonicalUrl,
            Publisher = string.IsNullOrWhiteSpace(raw.Publisher) ? "Báo điện tử" : raw.Publisher,
            Author = raw.Author,
            Description = raw.Description,
            ContentBody = raw.ContentBody,
            PublishedAtUtc = raw.PublishedAtUtc,
            Status = "Extracted"
        };

        _context.Articles.Add(article);
        await _context.SaveChangesAsync(cancellationToken);

        int order = 1;
        foreach (var imgRaw in raw.Images)
        {
            var hash = ComputeSha256(imgRaw.Url);
            var category = _classifierService.ClassifyImage(imgRaw.Caption, imgRaw.AltText, raw.ContentBody);

            var articleImage = new ArticleImage
            {
                ArticleId = article.Id,
                OriginalImageUrl = imgRaw.Url,
                Caption = imgRaw.Caption,
                AltText = imgRaw.AltText,
                ArticleImageOrder = order++,
                Width = imgRaw.Width,
                Height = imgRaw.Height,
                ImageHash = hash,
                // Rule: Default to Unknown, never auto-promote to Authorized
                RightsStatus = RightsStatus.Unknown,
                Category = category,
                Status = category == ImageCategory.Unsuitable ? "Excluded" : "Extracted"
            };

            // EF relationship fix-up already puts it in article.Images (adding it again listed every image twice)
            _context.ArticleImages.Add(articleImage);
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Saved article '{Title}' (ID: {Id}) with {Count} images in PostgreSQL",
            article.Title, article.Id, article.Images.Count);

        return MapToDto(article);
    }

    public async Task<IEnumerable<ArticleResponseDto>> GetArticlesAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var articles = await _context.Articles
            .AsNoTracking()
            .Include(a => a.Images)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return articles.Select(MapToDto);
    }

    public async Task<ArticleResponseDto?> GetArticleByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var article = await _context.Articles
            .AsNoTracking()
            .Include(a => a.Images)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        return article == null ? null : MapToDto(article);
    }

    public async Task<ArticleImageResponseDto?> UpdateArticleImageAsync(Guid articleId, Guid imageId, UpdateArticleImageDto dto, CancellationToken cancellationToken = default)
    {
        var image = await _context.ArticleImages
            .FirstOrDefaultAsync(i => i.Id == imageId && i.ArticleId == articleId, cancellationToken);

        if (image == null) return null;

        if (!string.IsNullOrWhiteSpace(dto.Caption))
        {
            image.Caption = dto.Caption;
        }

        if (dto.RightsStatus.HasValue)
        {
            image.RightsStatus = dto.RightsStatus.Value;
        }

        if (dto.Category.HasValue)
        {
            image.Category = dto.Category.Value;
        }

        if (dto.SelectedForVideo.HasValue)
        {
            image.Status = NewsStudio.Infrastructure.Common.ImageStatus.Toggle(image.Status, dto.SelectedForVideo.Value);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new ArticleImageResponseDto
        {
            Id = image.Id,
            ArticleId = image.ArticleId,
            OriginalImageUrl = image.OriginalImageUrl,
            LocalTempPath = image.LocalTempPath,
            Caption = image.Caption,
            AltText = image.AltText,
            ArticleImageOrder = image.ArticleImageOrder,
            Width = image.Width,
            Height = image.Height,
            ImageHash = image.ImageHash,
            RightsStatus = image.RightsStatus.ToString(),
            Category = image.Category.ToString(),
            Status = image.Status,
            ErrorMessage = image.ErrorMessage,
            SelectedForVideo = NewsStudio.Infrastructure.Common.ImageStatus.IsSelectedForVideo(image.Status)
        };
    }

    private static ArticleResponseDto MapToDto(Article article)
    {
        return new ArticleResponseDto
        {
            Id = article.Id,
            Title = article.Title,
            Url = article.Url,
            CanonicalUrl = article.CanonicalUrl,
            Publisher = article.Publisher,
            Author = article.Author,
            Description = article.Description,
            ContentBody = article.ContentBody,
            PublishedAtUtc = article.PublishedAtUtc,
            Status = article.Status,
            CreatedAtUtc = article.CreatedAtUtc,
            ErrorMessage = article.ErrorMessage,
            Images = article.Images.OrderBy(i => i.ArticleImageOrder).Select(i => new ArticleImageResponseDto
            {
                Id = i.Id,
                ArticleId = i.ArticleId,
                OriginalImageUrl = i.OriginalImageUrl,
                LocalTempPath = i.LocalTempPath,
                Caption = i.Caption,
                AltText = i.AltText,
                ArticleImageOrder = i.ArticleImageOrder,
                Width = i.Width,
                Height = i.Height,
                ImageHash = i.ImageHash,
                RightsStatus = i.RightsStatus.ToString(),
                Category = i.Category.ToString(),
                Status = i.Status,
                ErrorMessage = i.ErrorMessage,
                SelectedForVideo = NewsStudio.Infrastructure.Common.ImageStatus.IsSelectedForVideo(i.Status)
            }).ToList()
        };
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
