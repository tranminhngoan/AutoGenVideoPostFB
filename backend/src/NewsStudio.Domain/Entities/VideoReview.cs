using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

public class VideoReview : BaseEntity
{
    public Guid VideoProjectId { get; set; }
    public VideoProject? VideoProject { get; set; }

    public string ReviewedBy { get; set; } = "Editor";
    public string Decision { get; set; } = "Pending"; // Pending, Approved, Rejected, NeedsScriptRegen, NeedsAudioRegen, NeedsReRender
    public string? FeedbackNotes { get; set; }

    public int InternalQualityScore { get; set; } = 100;
    public bool SeriousViolationsFound { get; set; } = false;
    public string? ViolationDetails { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ErrorMessage { get; set; }
}
