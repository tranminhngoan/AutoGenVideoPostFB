using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

/// <summary>
/// A news item collected from a public RSS feed for the "Tin đang hot" list.
/// The hot score itself is computed at query time from these stored signals, so it keeps
/// decaying with age between refreshes.
/// </summary>
public class TrendingArticle : BaseEntity
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? ImageUrl { get; set; }

    /// <summary>Stable key of the feed, e.g. "vnexpress".</summary>
    public string SourceKey { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    /// <summary>"VN" or "World".</summary>
    public string Region { get; set; } = "VN";
    /// <summary>"vi" or "en".</summary>
    public string Language { get; set; } = "vi";

    public DateTime PublishedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }

    /// <summary>Came from an editor-curated "featured / top stories" feed.</summary>
    public bool IsFeatured { get; set; }

    /// <summary>Groups items about the same story across outlets (representative item's id).</summary>
    public Guid? ClusterId { get; set; }
    /// <summary>Number of distinct outlets covering the same story.</summary>
    public int CoverageCount { get; set; } = 1;
    /// <summary>Comma-separated names of the other outlets covering the story.</summary>
    public string? RelatedSources { get; set; }

    /// <summary>Comma-separated topic keys (see NewsTopics), e.g. "the-thao,giai-tri".</summary>
    public string? Topics { get; set; }

    /// <summary>Google Trends search term this story matches, if any.</summary>
    public string? TrendKeyword { get; set; }
    /// <summary>Approximate Google Trends search volume for the matched term.</summary>
    public int? TrendTraffic { get; set; }
}
