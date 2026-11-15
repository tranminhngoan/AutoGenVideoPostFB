using NewsStudio.Domain.Enums;

namespace NewsStudio.Application.DTOs;

public class ExtractArticleRequestDto
{
    public string Url { get; set; } = string.Empty;
}

public class ArticleResponseDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string CanonicalUrl { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string ContentBody { get; set; } = string.Empty;
    public DateTime? PublishedAtUtc { get; set; }
    public string Status { get; set; } = "Extracted";
    public DateTime CreatedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }

    public int ImageCount => Images.Count;
    public List<ArticleImageResponseDto> Images { get; set; } = new();
}

public class ArticleImageResponseDto
{
    public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public string OriginalImageUrl { get; set; } = string.Empty;
    public string? LocalTempPath { get; set; }
    public string? Caption { get; set; }
    public string? AltText { get; set; }
    public int ArticleImageOrder { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? ImageHash { get; set; }
    public string RightsStatus { get; set; } = "Unknown";
    public string Category { get; set; } = "Panorama";
    public string Status { get; set; } = "Extracted";
    public bool SelectedForVideo { get; set; } = true;
}

public class UpdateArticleImageDto
{
    public string? Caption { get; set; }
    public RightsStatus? RightsStatus { get; set; }
    public ImageCategory? Category { get; set; }
    public bool? SelectedForVideo { get; set; }
}
