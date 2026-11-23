namespace NewsStudio.Application.DTOs;

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
    public string? Location { get; set; }
    public List<string> Cast { get; set; } = new();
    public string? Speaker { get; set; }
    public string? Dialogue { get; set; }
    public string? Sound { get; set; }
    public bool HasVoice { get; set; }
    public double? VoiceSeconds { get; set; }
    public bool HasSfx { get; set; }
    public string Status { get; set; } = "Pending";
    public bool HasClip { get; set; }
    public decimal CostUsd { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public int Attempts { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>A character of the video's bible: fixed English look for every shot prompt, and the TTS voice.</summary>
public class StudioCharacterDto
{
    public string Name { get; set; } = string.Empty;
    public string Gender { get; set; } = "male";
    public string Look { get; set; } = string.Empty;
    public string Voice { get; set; } = string.Empty;
    /// <summary>Reading speed for TTS, e.g. "-6%" (slower, for emotional narration) … "+8%".</summary>
    public string? Speed { get; set; }
}

public class StudioLocationDto
{
    public string Name { get; set; } = string.Empty;
    public string Look { get; set; } = string.Empty;
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
    public string VideoModel { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = "9:16";
    public string Resolution { get; set; } = "720p";
    public string? StyleNotes { get; set; }
    public List<StudioCharacterDto> Characters { get; set; } = new();
    public List<StudioLocationDto> Locations { get; set; } = new();
    public bool SfxEnabled { get; set; } = true;
    public double SfxVolume { get; set; } = 0.7;
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
    public string? VideoModel { get; set; }
    public string? AspectRatio { get; set; }
    public string? Resolution { get; set; }
    public string? StyleNotes { get; set; }
    public List<StudioCharacterDto>? Characters { get; set; }
    public List<StudioLocationDto>? Locations { get; set; }
    public bool? SfxEnabled { get; set; }
    public double? SfxVolume { get; set; }
    public string? MusicMode { get; set; }
    public Guid? MusicTrackId { get; set; }
    public double? MusicVolume { get; set; }
    public string? Caption { get; set; }
}

public class StudioShotInputDto
{
    public Guid? Id { get; set; }
    public int DurationSeconds { get; set; } = 6;
    public string Action { get; set; } = string.Empty;
    public string Camera { get; set; } = string.Empty;
    public string? Location { get; set; }
    public List<string> Cast { get; set; } = new();
    public string? Speaker { get; set; }
    public string? Dialogue { get; set; }
    public string? Sound { get; set; }
}

public class StudioCostSummaryDto
{
    public decimal MonthSpentUsd { get; set; }
    public decimal MonthlyBudgetUsd { get; set; }
    public decimal MonthRemainingUsd { get; set; }
    public decimal AllTimeSpentUsd { get; set; }
    public int MonthVideoSeconds { get; set; }
    public List<StudioModelDto> Models { get; set; } = new();
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

public class StudioModelDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public Dictionary<string, decimal> PricePerSecondUsd { get; set; } = new();
}

public class StudioVoiceDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Gender { get; set; } = "male";
    public string Description { get; set; } = string.Empty;
}

public class StudioSettingsDto
{
    public decimal MonthlyBudgetUsd { get; set; }
    public bool VideoConfigured { get; set; }
    public string VideoModel { get; set; } = string.Empty;
    public List<StudioModelDto> Models { get; set; } = new();
    public List<StudioVoiceDto> Voices { get; set; } = new();
    public decimal VoicePricePerSecondUsd { get; set; }
    public bool SfxConfigured { get; set; }
    public decimal SfxPricePerSecondUsd { get; set; }
}

/// <summary>A finished script from the script library, imported shot for shot (no AI planning).</summary>
public class StudioScriptImportDto
{
    public string Title { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Synopsis { get; set; }
    public string? Mood { get; set; }
    public string? Region { get; set; }
    public string? Era { get; set; }
    public string? Hook { get; set; }
    public string? Ending { get; set; }
    /// <summary>The full readable script, kept on the video for reference.</summary>
    public string? Script { get; set; }
    public List<StudioSceneImportDto> Scenes { get; set; } = new();

    // video options chosen before creating
    public Guid? FacebookAccountId { get; set; }
    public string? VideoModel { get; set; }
    public string? Resolution { get; set; }
    public string? AspectRatio { get; set; }
    public string? StyleNotes { get; set; }
    public string? NarratorVoice { get; set; }
    public string? NarratorSpeed { get; set; }
    public string? MusicMode { get; set; }
    public Guid? MusicTrackId { get; set; }
    public double? MusicVolume { get; set; }
    public bool? SfxEnabled { get; set; }
    public double? SfxVolume { get; set; }
    /// <summary>Start generating the clips as soon as the script is ready (after the budget check).</summary>
    public bool AutoGenerate { get; set; }
}

public class StudioSceneImportDto
{
    public string? Title { get; set; }
    public string? VisualVi { get; set; }
    public string? PromptEn { get; set; }
    public string? Voiceover { get; set; }
    public string? Sound { get; set; }
    public string? Camera { get; set; }
    public int DurationSeconds { get; set; } = 15;
}
