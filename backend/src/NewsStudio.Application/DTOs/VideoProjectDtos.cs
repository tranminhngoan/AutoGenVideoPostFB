using NewsStudio.Domain.Enums;

namespace NewsStudio.Application.DTOs;

public class CreateVideoProjectDto
{
    public string Title { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string Publisher { get; set; } = "Online News";
    public int TargetDurationSeconds { get; set; } = 105; // upper bound; the AI shortens it for short articles
    public string? AiProvider { get; set; } = "Gemini";
    public string? Voice { get; set; } // null = server default (Tts:DefaultVoice, natural Gemini voice)
    public string? VoiceSpeed { get; set; }
    /// <summary>
    /// Page the video is for (its name is shown on the video; its topic sets the colours).
    /// Null: the only connected page, if there is exactly one.
    /// </summary>
    public Guid? FacebookAccountId { get; set; }
}

public class VideoProjectResponseDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TargetDurationSeconds { get; set; }
    public double? ActualDurationSeconds { get; set; }
    public string Resolution { get; set; } = "1080x1920";
    public string AspectRatio { get; set; } = "9:16";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
    public string? AiProvider { get; set; }
    public string? Voice { get; set; }
    public string? VoiceSpeed { get; set; }
    public Guid? FacebookAccountId { get; set; }
    public string? PageName { get; set; }
    public string? Topic { get; set; }

    public int SceneCount { get; set; }
    public List<VideoSceneResponseDto> Scenes { get; set; } = new();
    public List<ProcessingJobResponseDto> Jobs { get; set; } = new();
}

public class VideoSceneResponseDto
{
    public Guid Id { get; set; }
    public int SceneNumber { get; set; }
    public double StartTimeSeconds { get; set; }
    public double EndTimeSeconds { get; set; }
    public string VoiceText { get; set; } = string.Empty;
    public string OnScreenText { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string MotionType { get; set; } = "ZoomIn";
    public string TransitionType { get; set; } = "Fade";
    public string ScenePurpose { get; set; } = "Fact";
}

public class ProcessingJobResponseDto
{
    public Guid Id { get; set; }
    public Guid VideoProjectId { get; set; }
    public string? HangfireJobId { get; set; }
    public string Step { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public int ProgressPercentage { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
}
