namespace NewsStudio.Application.DTOs;

/// <summary>View statistics of every connected Facebook Page (dashboard).</summary>
public class PageStatsOverviewDto
{
    public int Days { get; set; }
    /// <summary>Vietnam dates (yyyy-MM-dd) of the chart, oldest first; each page's DailyViews is aligned to it.</summary>
    public List<string> Dates { get; set; } = new();
    /// <summary>Views gained per day across all pages, aligned to <see cref="Dates"/>.</summary>
    public List<long> DailyViews { get; set; } = new();
    public PageStatsTotalsDto Totals { get; set; } = new();
    public List<PageViewStatsDto> Pages { get; set; } = new();
    public DateTime? LastSyncUtc { get; set; }
}

public class PageStatsTotalsDto
{
    public int Videos { get; set; }
    public long TotalViews { get; set; }
    public long ViewsToday { get; set; }
    public long Views7Days { get; set; }
    public long ViewsPrevious7Days { get; set; }

    /// <summary>Instagram Reels (all pages): videos, lifetime views, today, last 7 days.</summary>
    public int InstagramVideos { get; set; }
    public long InstagramViews { get; set; }
    public long InstagramViewsToday { get; set; }
    public long InstagramViews7Days { get; set; }

    /// <summary>Followers of all pages / Instagram accounts now, and the change over the last 7 days.</summary>
    public long Followers { get; set; }
    public long FollowersChange7Days { get; set; }
    public long InstagramFollowers { get; set; }
    public long InstagramFollowersChange7Days { get; set; }
}

public class PageViewStatsDto
{
    public Guid FacebookAccountId { get; set; }
    public string PageName { get; set; } = string.Empty;
    public string? Topic { get; set; }
    public string? TopicName { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Reels published on this page.</summary>
    public int Videos { get; set; }
    /// <summary>Reels whose numbers have been read from Facebook at least once.</summary>
    public int VideosWithStats { get; set; }
    public long TotalViews { get; set; }
    public long ViewsToday { get; set; }
    public long Views7Days { get; set; }
    public long ViewsPrevious7Days { get; set; }
    public long AverageViewsPerVideo { get; set; }
    public long Reach { get; set; }
    public long Likes { get; set; }
    public long Comments { get; set; }
    public long Shares { get; set; }
    public List<long> DailyViews { get; set; } = new();
    public PageTopVideoDto? TopVideo { get; set; }

    public DateTime? LastSyncedUtc { get; set; }
    /// <summary>Why the numbers couldn't be read from Facebook (e.g. the token lacks read_insights).</summary>
    public string? SyncError { get; set; }

    /// <summary>Instagram account linked to the page and its Reels' numbers.</summary>
    public string? InstagramUsername { get; set; }
    public int InstagramVideos { get; set; }
    public long InstagramViews { get; set; }
    public long InstagramViewsToday { get; set; }
    public long InstagramViews7Days { get; set; }
    public long InstagramLikes { get; set; }
    public long InstagramComments { get; set; }
    /// <summary>Views gained per day on Instagram, aligned to the overview's dates.</summary>
    public List<long> InstagramDailyViews { get; set; } = new();
    /// <summary>Why the Instagram numbers couldn't be read (e.g. the token lacks instagram_manage_insights).</summary>
    public string? InstagramSyncError { get; set; }

    /// <summary>Page followers now (null until first read) and gained over the last 7 days.</summary>
    public long? Followers { get; set; }
    public long? FollowersChange7Days { get; set; }
    public long? InstagramFollowers { get; set; }
    public long? InstagramFollowersChange7Days { get; set; }

    /// <summary>Latest posts of the page with their numbers (Facebook and Instagram), newest first.</summary>
    public List<PagePostStatsDto> RecentPosts { get; set; } = new();
}

/// <summary>One post's lifetime numbers (news Reel, Video AI video, or a post made by hand on the page).</summary>
public class PagePostStatsDto
{
    public string Title { get; set; } = string.Empty;
    public string Source { get; set; } = "Tool";
    public DateTime? PublishedAtUtc { get; set; }
    public string? FacebookUrl { get; set; }
    public string? InstagramUrl { get; set; }
    public long? Views { get; set; }
    public long? Likes { get; set; }
    public long? Comments { get; set; }
    public long? Shares { get; set; }
    public long? InstagramViews { get; set; }
    public long? InstagramLikes { get; set; }
    public long? InstagramComments { get; set; }
}

/// <summary>A viewer's comment with the reply drafted by AI (engagement inbox).</summary>
public class PostCommentDto
{
    public Guid Id { get; set; }
    public string Network { get; set; } = "facebook";
    public string? PageName { get; set; }
    public string PostTitle { get; set; } = string.Empty;
    public string? PostUrl { get; set; }
    public string? Author { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CommentedAtUtc { get; set; }
    public string Status { get; set; } = "New";
    public string? SuggestedReply { get; set; }
    public string? SentReply { get; set; }
    public string? ReplyError { get; set; }
}

/// <summary>A comment read from Facebook / Instagram.</summary>
public class RemoteCommentDto
{
    public string Network { get; set; } = "facebook";
    public string Id { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedUtc { get; set; }
    /// <summary>Written by the page itself (its own first comment or replies).</summary>
    public bool FromPage { get; set; }
}

/// <summary>A post found on a page / its Instagram account (also ones made by hand).</summary>
public class DiscoveredPostDto
{
    public string Network { get; set; } = "facebook"; // facebook | instagram
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public string? Text { get; set; }
    public string? Permalink { get; set; }
}

public class PageTopVideoDto
{
    public Guid? VideoProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? PostUrl { get; set; }
    public long Views { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}

public class PageStatsSyncResultDto
{
    public int PostsChecked { get; set; }
    public int PostsUpdated { get; set; }
    public List<string> Errors { get; set; } = new();
}
