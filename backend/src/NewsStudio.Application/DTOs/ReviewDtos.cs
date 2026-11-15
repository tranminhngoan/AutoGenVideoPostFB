using System;
using System.Collections.Generic;

namespace NewsStudio.Application.DTOs;

public class QualityCheckItemDto
{
    public string CheckCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public int Score { get; set; } // 0 - 10
    public bool IsFatal { get; set; }
    public string Details { get; set; } = string.Empty;
    public string? Recommendation { get; set; }
}

public class QualityAssessmentResultDto
{
    public int TotalScore { get; set; } // 0 - 100
    public bool HasFatalViolations { get; set; }
    public bool CanPublish { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> FatalViolations { get; set; } = new();
    public List<QualityCheckItemDto> CheckItems { get; set; } = new();
    public DateTime EvaluatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Notice { get; set; } = "Điểm kiểm định nội bộ là tiêu chuẩn biên tập của AI News Studio, không phản ánh thuật toán của Facebook.";
}

public class VideoReviewDto
{
    public Guid Id { get; set; }
    public Guid VideoProjectId { get; set; }
    public string ReviewedBy { get; set; } = "Editor";
    public string Decision { get; set; } = "Pending"; // Pending, Approved, Rejected, NeedsScriptRegen, NeedsAudioRegen, NeedsReRender
    public string? FeedbackNotes { get; set; }
    public int InternalQualityScore { get; set; } = 100;
    public bool SeriousViolationsFound { get; set; } = false;
    public string? ViolationDetails { get; set; }
    public bool CanPublish { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public QualityAssessmentResultDto? Assessment { get; set; }
}

public class SubmitReviewDecisionRequestDto
{
    public string Decision { get; set; } = "Approved"; // Approved, Rejected, NeedsScriptRegen, NeedsAudioRegen, NeedsReRender
    public string ReviewedBy { get; set; } = "Editor";
    public string? FeedbackNotes { get; set; }
}

public class UpdateVideoMetadataRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string? Hook { get; set; }
    public string? Caption { get; set; }
}

public class ReviewActionRequestDto
{
    public string Action { get; set; } = string.Empty; // RegenerateScript, RegenerateAudio, ReRender
    public string? Notes { get; set; }
}
