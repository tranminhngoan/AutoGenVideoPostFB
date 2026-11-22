namespace NewsStudio.Application.DTOs;

public class StudioAssetDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "Character";
    public string Description { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class StudioMusicDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Mood { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public long SizeBytes { get; set; }
    public int UseCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class StudioShotDto
{
    public Guid Id { get; set; }
    public int Index { get; set; }
    public int DurationSeconds { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Camera { get; set; } = string.Empty;
    public string? Speaker { get; set; }
    public string? Dialogue { get; set; }
    public string? Sound { get; set; }
    public List<Guid> AssetIds { get; set; } = new();
    public bool ContinueFromPrevious { get; set; }
    public string Status { get; set; } = "Pending";
    public bool HasClip { get; set; }
    public decimal CostUsd { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public int Attempts { get; set; }
    public string? ErrorMessage { get; set; }
}

public class StudioVideoSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid? FacebookAccountId { get; set; }
    public string? PageName { get; set; }
    public int TargetDurationSeconds { get; set; }
    public string Status { get; set; } = "Draft";
    public string? Progress { get; set; }
    public int ShotCount { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public decimal ActualCostUsd { get; set; }
    public bool HasFinal { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}

public class StudioVideoDto : StudioVideoSummaryDto
{
    public string Script { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = "9:16";
    public string Resolution { get; set; } = "720p";
    public string? StyleNotes { get; set; }
    public List<Guid> AssetIds { get; set; } = new();
    public string MusicMode { get; set; } = "Auto";
    public Guid? MusicTrackId { get; set; }
    public string? MusicName { get; set; }
    public double MusicVolume { get; set; }
    public string? PlanNotes { get; set; }
    public string? Caption { get; set; }
    public double? FinalDurationSeconds { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PublishedPostId { get; set; }
    public List<StudioShotDto> Shots { get; set; } = new();
}

/// <summary>Create / update a video (all fields optional on update).</summary>
public class StudioVideoInputDto
{
    public string? Title { get; set; }
    public Guid? FacebookAccountId { get; set; }
    public string? Script { get; set; }
    public int? TargetDurationSeconds { get; set; }
    public string? AspectRatio { get; set; }
    public string? Resolution { get; set; }
    public string? StyleNotes { get; set; }
    public List<Guid>? AssetIds { get; set; }
    public string? MusicMode { get; set; }
    public Guid? MusicTrackId { get; set; }
    public double? MusicVolume { get; set; }
    public string? Caption { get; set; }
}

public class StudioShotInputDto
{
    public Guid? Id { get; set; }
    public int DurationSeconds { get; set; } = 8;
    public string Action { get; set; } = string.Empty;
    public string Camera { get; set; } = string.Empty;
    public string? Speaker { get; set; }
    public string? Dialogue { get; set; }
    public string? Sound { get; set; }
    public List<Guid> AssetIds { get; set; } = new();
    public bool ContinueFromPrevious { get; set; }
}

public class StudioCostSummaryDto
{
    public decimal MonthSpentUsd { get; set; }
    public decimal MonthlyBudgetUsd { get; set; }
    public decimal MonthRemainingUsd { get; set; }
    public decimal AllTimeSpentUsd { get; set; }
    public int MonthVideoSeconds { get; set; }
    public Dictionary<string, decimal> PricePerSecondUsd { get; set; } = new();
    public List<StudioCostVideoDto> Videos { get; set; } = new();
    public List<StudioCostRecordDto> Recent { get; set; } = new();
}

public class StudioCostVideoDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal CostUsd { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class StudioCostRecordDto
{
    public DateTime CreatedAtUtc { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public double? Seconds { get; set; }
    public decimal CostUsd { get; set; }
    public string? Note { get; set; }
    public string? VideoTitle { get; set; }
}

public class StudioSettingsDto
{
    public decimal MonthlyBudgetUsd { get; set; }
    public bool VideoConfigured { get; set; }
    public string VideoModel { get; set; } = string.Empty;
}
