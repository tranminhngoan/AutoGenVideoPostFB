using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

public class VideoScript : BaseEntity
{
    public Guid VideoProjectId { get; set; }
    public VideoProject? VideoProject { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Hook { get; set; } = string.Empty;
    public string VoiceOverScript { get; set; } = string.Empty;
    public string KeyFacts { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public string Analysis { get; set; } = string.Empty;
    public string Conclusion { get; set; } = string.Empty;
    public string SourceAttribution { get; set; } = string.Empty;

    /// <summary>Subjects (A–E), key claims with their article sentence, chart data, check results (JSON of FactSheetDto).</summary>
    public string? FactSheetJson { get; set; }

    public string AiProvider { get; set; } = "Gemini";
    public string ModelName { get; set; } = "gemini-1.5-flash";
    public int TokenUsage { get; set; }
    public decimal? EstimatedCostUsd { get; set; }

    public string Status { get; set; } = "Completed";
    public string? ErrorMessage { get; set; }
}
