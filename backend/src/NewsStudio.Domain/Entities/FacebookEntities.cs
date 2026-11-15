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

    // Navigation
    public ICollection<VideoMetric> Metrics { get; set; } = new List<VideoMetric>();
}
