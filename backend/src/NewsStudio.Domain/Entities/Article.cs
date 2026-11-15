using NewsStudio.Domain.Common;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Domain.Entities;

public class Article : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string CanonicalUrl { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string ContentBody { get; set; } = string.Empty;
    public DateTime? PublishedAtUtc { get; set; }
    public string Status { get; set; } = "Extracted";
    public string? ErrorMessage { get; set; }

    // Navigation
    public ICollection<ArticleImage> Images { get; set; } = new List<ArticleImage>();
    public ICollection<VideoProject> VideoProjects { get; set; } = new List<VideoProject>();
}
