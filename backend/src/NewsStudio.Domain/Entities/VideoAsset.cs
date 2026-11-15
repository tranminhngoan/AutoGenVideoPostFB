using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

public class VideoAsset : BaseEntity
{
    public Guid VideoProjectId { get; set; }
    public VideoProject? VideoProject { get; set; }

    public string AssetType { get; set; } = "Image"; // Image, Audio, Subtitle, VideoClip, FinalVideo
    public string LocalTempPath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public string? Sha256Hash { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsCleanedUp { get; set; } = false;
    public DateTime? CleanedUpAtUtc { get; set; }

    public string Status { get; set; } = "Active";
    public string? ErrorMessage { get; set; }
}
