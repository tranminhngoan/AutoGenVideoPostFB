namespace NewsStudio.Application.DTOs;

public class TrendingArticleDto
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? ImageUrl { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string Region { get; set; } = "VN";
    public string Language { get; set; } = "vi";
    public DateTime PublishedAtUtc { get; set; }
    public bool IsFeatured { get; set; }
    public int CoverageCount { get; set; }
    public List<string> RelatedSources { get; set; } = new();
    public string? TrendKeyword { get; set; }
    public int? TrendTraffic { get; set; }
    /// <summary>0–100, recomputed on every read so it decays with age.</summary>
    public double HotScore { get; set; }
    /// <summary>Why it is ranked hot, in plain Vietnamese.</summary>
    public List<string> Reasons { get; set; } = new();
}

public class TrendingQuery
{
    /// <summary>"all" | "VN" | "World"</summary>
    public string Region { get; set; } = "all";
    public string? Source { get; set; }
    public string? Search { get; set; }
    /// <summary>Only items published within this many hours.</summary>
    public int Hours { get; set; } = 24;
    public int Limit { get; set; } = 60;
}

public class TrendingSourceDto
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = "VN";
    public string Language { get; set; } = "vi";
    public int ItemCount { get; set; }
}

public class TrendingRefreshResultDto
{
    public DateTime StartedAtUtc { get; set; }
    public DateTime FinishedAtUtc { get; set; }
    public int FeedsOk { get; set; }
    public int FeedsFailed { get; set; }
    public List<string> FailedFeeds { get; set; } = new();
    public int ItemsFetched { get; set; }
    public int NewItems { get; set; }
    public int TrendKeywords { get; set; }
    public int TotalStored { get; set; }
}

public class TrendingOverviewDto
{
    public List<TrendingArticleDto> Items { get; set; } = new();
    public List<TrendingSourceDto> Sources { get; set; } = new();
    public TrendingRefreshResultDto? LastRefresh { get; set; }
    public List<string> TrendKeywords { get; set; } = new();
}
