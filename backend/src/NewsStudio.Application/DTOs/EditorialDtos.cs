namespace NewsStudio.Application.DTOs;

/// <summary>
/// AI editorial pass before posting: does the video's headline / text match the original article, plus the
/// Facebook caption and the page's first comment.
/// </summary>
public class EditorialPassDto
{
    /// <summary>False when the headline or text says something the article doesn't (wrong facts, misleading).</summary>
    public bool Consistent { get; set; } = true;
    /// <summary>What doesn't match, in Vietnamese.</summary>
    public List<string> Issues { get; set; } = new();
    /// <summary>Ready-to-post caption (hook, key facts, source, hashtags).</summary>
    public string Caption { get; set; } = string.Empty;
    /// <summary>First comment: an open question about the story + link to the article.</summary>
    public string FirstComment { get; set; } = string.Empty;
    public DateTime CheckedAtUtc { get; set; }
}

/// <summary>What this page's audience watches most: by posting hour, kind of story and region.</summary>
public class PageInsightsDto
{
    public Guid FacebookAccountId { get; set; }
    /// <summary>Videos (posted at least a day ago, with view numbers) the figures are based on.</summary>
    public int Samples { get; set; }
    public int MinSamples { get; set; }
    public bool EnoughData { get; set; }
    public double AverageViews { get; set; }
    /// <summary>Vietnam hour 0–23 → videos posted in that hour.</summary>
    public List<InsightBucketDto> Hours { get; set; } = new();
    /// <summary>Kind of story (topic key) → videos.</summary>
    public List<InsightBucketDto> Topics { get; set; } = new();
    /// <summary>"VN" / "World".</summary>
    public List<InsightBucketDto> Regions { get; set; } = new();
    /// <summary>Suggested posting times ("HH:mm"), as many as the page posts per day; empty without enough data.</summary>
    public List<string> SuggestedSlots { get; set; } = new();
}

public class InsightBucketDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Videos { get; set; }
    public double AverageViews { get; set; }
    /// <summary>AverageViews / page average (1 = average).</summary>
    public double Ratio { get; set; }
}
