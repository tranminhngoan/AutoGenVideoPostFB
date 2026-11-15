using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

public class VideoScene : BaseEntity
{
    public Guid VideoProjectId { get; set; }
    public VideoProject? VideoProject { get; set; }

    public int SceneNumber { get; set; }
    public double StartTimeSeconds { get; set; }
    public double EndTimeSeconds { get; set; }
    public double DurationSeconds => EndTimeSeconds - StartTimeSeconds;

    public string VoiceText { get; set; } = string.Empty;
    public string OnScreenText { get; set; } = string.Empty;

    public Guid? SelectedArticleImageId { get; set; }
    public ArticleImage? SelectedArticleImage { get; set; }

    public string? ImageCaption { get; set; }
    public string MotionType { get; set; } = "ZoomIn";
    public string TransitionType { get; set; } = "Fade";
    public string ScenePurpose { get; set; } = "Fact";

    public string Status { get; set; } = "Planned";
    public string? ErrorMessage { get; set; }
}
