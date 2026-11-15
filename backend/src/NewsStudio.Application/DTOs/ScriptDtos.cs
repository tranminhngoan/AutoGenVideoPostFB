using NewsStudio.Domain.Enums;

namespace NewsStudio.Application.DTOs;

public class EditorialScriptPromptInput
{
    public Guid ProjectId { get; set; }
    public string ArticleTitle { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string? Author { get; set; }
    public DateTime? PublishedDate { get; set; }
    public string CanonicalUrl { get; set; } = string.Empty;
    public string ContentBody { get; set; } = string.Empty;
    public List<ArticleImageInputDto> AvailableImages { get; set; } = new();
    public int TargetDurationSeconds { get; set; } = 55;
    public string Tone { get; set; } = "Chính luận sắc bén, súc tích, khách quan";
}

public class ArticleImageInputDto
{
    public Guid Id { get; set; }
    public string OriginalImageUrl { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public string? AltText { get; set; }
    public int Order { get; set; }
    public string Category { get; set; } = "Panorama";
    public string RightsStatus { get; set; } = "Unknown";
    public bool SelectedForVideo { get; set; } = true;
}

public class EditorialScriptResult
{
    public string VideoTitle { get; set; } = string.Empty;
    public string Hook { get; set; } = string.Empty;
    public string VoiceOverScript { get; set; } = string.Empty;
    public string KeyFacts { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public string Analysis { get; set; } = string.Empty;
    public string Conclusion { get; set; } = string.Empty;
    public string SourceAttribution { get; set; } = string.Empty;
    public int EstimatedReadingSeconds { get; set; }
    public int WordCount { get; set; }

    public string AiProvider { get; set; } = "Gemini";
    public string ModelName { get; set; } = "gemini-1.5-flash";
    public string PromptVersion { get; set; } = "v1.0-editorial";
    public int TokenUsage { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
}

public class ScriptPlanningInput
{
    public Guid ProjectId { get; set; }
    public EditorialScriptResult Script { get; set; } = new();
    public List<ArticleImageInputDto> AvailableImages { get; set; } = new();
    public int TargetDurationSeconds { get; set; } = 55;
}

public class VideoScenePlanDto
{
    public Guid? Id { get; set; }
    public int SceneNumber { get; set; }
    public double StartTimeSeconds { get; set; }
    public double EndTimeSeconds { get; set; }
    public double DurationSeconds => Math.Round(EndTimeSeconds - StartTimeSeconds, 2);

    public string VoiceText { get; set; } = string.Empty;
    public string OnScreenText { get; set; } = string.Empty;

    public Guid? SelectedArticleImageId { get; set; }
    public string? ImageUrl { get; set; }
    public string? ImageCaption { get; set; }
    public string VisualType { get; set; } = "Image"; // "Image", "Typography", "MetricChart", "Timeline"

    public string MotionType { get; set; } = "ZoomIn"; // "ZoomIn", "ZoomOut", "PanLeft", "PanRight", "KenBurns"
    public string TransitionType { get; set; } = "Fade"; // "Fade", "Slide", "Cut"
    public string ScenePurpose { get; set; } = "Fact"; // "Hook", "Fact", "Context", "Analysis", "Conclusion"
}

public class GenerateScriptRequestDto
{
    public string? AiProvider { get; set; } // "Gemini" | "Claude"
    public string? ModelName { get; set; }
    public int? TargetDurationSeconds { get; set; }
}

public class UpdateScriptRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string Hook { get; set; } = string.Empty;
    public string VoiceOverScript { get; set; } = string.Empty;
    public string KeyFacts { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public string Analysis { get; set; } = string.Empty;
    public string Conclusion { get; set; } = string.Empty;
    public string SourceAttribution { get; set; } = string.Empty;
}

public class VideoScriptResponseDto
{
    public Guid Id { get; set; }
    public Guid VideoProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Hook { get; set; } = string.Empty;
    public string VoiceOverScript { get; set; } = string.Empty;
    public string KeyFacts { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public string Analysis { get; set; } = string.Empty;
    public string Conclusion { get; set; } = string.Empty;
    public string SourceAttribution { get; set; } = string.Empty;

    public string AiProvider { get; set; } = "Gemini";
    public string ModelName { get; set; } = "gemini-1.5-flash";
    public int TokenUsage { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public string Status { get; set; } = "Completed";
    public int EstimatedReadingSeconds { get; set; }
    public int WordCount { get; set; }
}

public class UpdateScenesRequestDto
{
    public List<VideoScenePlanDto> Scenes { get; set; } = new();
}
