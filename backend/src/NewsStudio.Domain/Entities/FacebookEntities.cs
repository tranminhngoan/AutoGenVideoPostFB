using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

public class FacebookAccount : BaseEntity
{
    public string PageId { get; set; } = string.Empty;
    public string PageName { get; set; } = string.Empty;
    public string EncryptedAccessToken { get; set; } = string.Empty;
    public DateTime? TokenExpiresAtUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public string Status { get; set; } = "Active";
    public string? ErrorMessage { get; set; }

    /// <summary>Instagram professional account linked to this page (Reels are also posted there).</summary>
    public string? InstagramAccountId { get; set; }
    public string? InstagramUsername { get; set; }

    // Navigation
    public ICollection<FacebookPost> FacebookPosts { get; set; } = new List<FacebookPost>();
}

public class FacebookPost : BaseEntity
{
    /// <summary>The news video it came from; null for a Video AI post or a post found on the page.</summary>
    public Guid? VideoProjectId { get; set; }
    public VideoProject? VideoProject { get; set; }
    /// <summary>The Video AI (studio) video it came from.</summary>
    public Guid? StudioVideoId { get; set; }
    /// <summary>Title shown in the stats when there is no news video (studio title, or the post's text).</summary>
    public string? Title { get; set; }
    /// <summary>Tool (news), Studio (Video AI), Discovered (posted by hand on Facebook / Instagram, found on the page).</summary>
    public string Source { get; set; } = "Tool";

    public Guid? FacebookAccountId { get; set; }
    public FacebookAccount? FacebookAccount { get; set; }

    public string? FacebookPostId { get; set; }
    public string? FacebookVideoId { get; set; }
    public string? UploadSessionId { get; set; }
    
    public string UploadStatus { get; set; } = "Pending"; // Pending, SessionCreated, Uploading, Processing, Published, Failed
    public DateTime? PublishedAtUtc { get; set; }

    public string Status { get; set; } = "Pending";
    public string? ErrorMessage { get; set; }

    /// <summary>The page's own first comment under the Reel (question + link to the article).</summary>
    public string? FirstCommentId { get; set; }
    public string? FirstCommentError { get; set; }

    /// <summary>The question the page posted as first comment (Video AI / hand-made posts), and its Instagram copy.</summary>
    public string? FirstCommentText { get; set; }
    public string? InstagramFirstCommentId { get; set; }
    public string? InstagramFirstCommentError { get; set; }

    /// <summary>The same Reel posted on the page's linked Instagram account.</summary>
    public string? InstagramMediaId { get; set; }
    public string? InstagramPermalink { get; set; }
    public string? InstagramError { get; set; }
    public ICollection<InstagramMetric> InstagramMetrics { get; set; } = new List<InstagramMetric>();

    // Navigation
    public ICollection<VideoMetric> Metrics { get; set; } = new List<VideoMetric>();
}

/// <summary>A viewer's comment on one of the page's posts (Facebook or Instagram), with the reply drafted by AI.</summary>
public class PostComment : BaseEntity
{
    public Guid FacebookPostId { get; set; }
    public FacebookPost? FacebookPost { get; set; }
    public string Network { get; set; } = "facebook"; // facebook | instagram
    public string ExternalId { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CommentedAtUtc { get; set; }
    /// <summary>New (waiting), Sent (answered), Dismissed (owner skipped it), Skipped (spam — AI found nothing to answer).</summary>
    public string Status { get; set; } = "New";
    public string? SuggestedReply { get; set; }
    public string? SentReply { get; set; }
    public string? ReplyId { get; set; }
    public string? ReplyError { get; set; }
    public DateTime? RepliedAtUtc { get; set; }
}

/// <summary>Followers of a page and of its linked Instagram account, one snapshot per (Vietnam) day.</summary>
public class PageFollowerSnapshot : BaseEntity
{
    public Guid FacebookAccountId { get; set; }
    public DateOnly Date { get; set; }
    public long FacebookFollowers { get; set; }
    public long? InstagramFollowers { get; set; }
    public DateTime CollectedAtUtc { get; set; }
}

/// <summary>
/// Lifetime numbers of a Reel on Instagram, one snapshot per (Vietnam) day per post: later reads the same day
/// update it, so day-by-day gains can be computed like for Facebook.
/// </summary>
public class InstagramMetric : BaseEntity
{
    public Guid FacebookPostId { get; set; }
    public FacebookPost? FacebookPost { get; set; }

    public long Views { get; set; }
    public long Reach { get; set; }
    public long Likes { get; set; }
    public long Comments { get; set; }
    public long Shares { get; set; }
    public long Saved { get; set; }
    public DateTime CollectedAtUtc { get; set; }
}
