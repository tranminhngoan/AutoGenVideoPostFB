namespace NewsStudio.Application.DTOs;

/// <summary>One story of a daily digest.</summary>
public class DailyDigestItemDto
{
    /// <summary>Original headline from the outlet.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Short headline written on the picture.</summary>
    public string Headline { get; set; } = string.Empty;
    /// <summary>1–2 sentence summary written on the picture.</summary>
    public string Summary { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    /// <summary>The story's own picture (from the outlet).</summary>
    public string? ImageUrl { get; set; }
    public int CoverageCount { get; set; }
    public double HotScore { get; set; }
}

public class DailyDigestRunDto
{
    public Guid Id { get; set; }
    public Guid? FacebookAccountId { get; set; }
    public string? PageName { get; set; }
    public string DigestDate { get; set; } = string.Empty;
    /// <summary>"Daily" or "Weekly".</summary>
    public string Kind { get; set; } = "Daily";
    public bool IsPreview { get; set; }
    /// <summary>Working | Published | Partial | Failed | Preview</summary>
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Caption { get; set; }
    public List<DailyDigestItemDto> Items { get; set; } = new();
    /// <summary>API links of the wall-post pictures (cover first).</summary>
    public List<string> PostImages { get; set; } = new();
    /// <summary>API links of the Story frames (cover first).</summary>
    public List<string> StoryImages { get; set; } = new();
    public string? PostId { get; set; }
    public string? PostUrl { get; set; }
    public string? PostError { get; set; }
    public string? StoryPostId { get; set; }
    public string? StoryError { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}
