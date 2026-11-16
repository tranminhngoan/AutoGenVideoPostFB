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
    public Guid VideoProjectId { get; set; }
    public VideoProject? VideoProject { get; set; }

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

    /// <summary>The same Reel posted on the page's linked Instagram account.</summary>
    public string? InstagramMediaId { get; set; }
    public string? InstagramPermalink { get; set; }
    public string? InstagramError { get; set; }

    // Navigation
    public ICollection<VideoMetric> Metrics { get; set; } = new List<VideoMetric>();
}
