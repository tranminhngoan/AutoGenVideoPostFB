using System.Text.Json.Nodes;

namespace NewsStudio.Application.DTOs;

// ───────────── settings / engine ─────────────

public class AiStudioSettingsDto
{
    /// <summary>Default local model for new analyses: "4b" (balanced) or "8b" (more accurate, ~4× slower).</summary>
    public string DefaultModel { get; set; } = "4b";
    /// <summary>CPU threads for the local model (0 = automatic).</summary>
    public int Threads { get; set; }
    /// <summary>"auto" = the graphics card (Vulkan) when it works, else CPU; "cpu" = CPU only.</summary>
    public string Gpu { get; set; } = "auto";
    public int MaxFramesPerScene { get; set; } = 8;
    /// <summary>Speech transcript with whisper.cpp when installed (helps the model understand the scene).</summary>
    public bool Transcribe { get; set; } = true;
    public string? WorkDir { get; set; }
    public int MaxRamMb { get; set; } = 16384;
    /// <summary>Days the reference video stays in storage after it is uploaded (the analysis is kept).</summary>
    public int SourceRetentionDays { get; set; } = 7;
    /// <summary>Days generated clips / joined videos stay in storage.</summary>
    public int ClipRetentionDays { get; set; } = 14;
}

public class AiStudioEngineStatusDto
{
    public bool Ready { get; set; }
    public string? Error { get; set; }
    public DateTime? CheckedAtUtc { get; set; }
    public string? Machine { get; set; }
    public string? Home { get; set; }
    public string? Python { get; set; }
    public string? OpenCv { get; set; }
    public Dictionary<string, bool> Backends { get; set; } = new();
    public Dictionary<string, bool> Models { get; set; } = new();
    public bool Whisper { get; set; }
    public int CpuThreads { get; set; }
    public double RamGb { get; set; }
    public double? FreeDiskGb { get; set; }
    public string? Gpu { get; set; }
    /// <summary>Measured generation speed of each local model (tokens / s) and its backend.</summary>
    public Dictionary<string, double> TokensPerSecond { get; set; } = new();
    public Dictionary<string, string> BenchBackend { get; set; } = new();
}

public class AiStudioBudgetDto
{
    public decimal MonthSpentUsd { get; set; }
    public decimal MonthlyBudgetUsd { get; set; }
    public decimal MonthRemainingUsd { get; set; }
}

public class AiStudioTopicDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class AiStudioVideoModelDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public Dictionary<string, decimal> PricePerSecondUsd { get; set; } = new();
}

public class AiStudioOverviewDto
{
    public bool PcOnline { get; set; }
    public bool PcUpdating { get; set; }
    public string? PcMachine { get; set; }
    public AiStudioEngineStatusDto Engine { get; set; } = new();
    public bool SeedanceConfigured { get; set; }
    public List<AiStudioVideoModelDto> VideoModels { get; set; } = new();
    public List<AiStudioTopicDto> Topics { get; set; } = new();
    /// <summary>Narrator voices (Gemini TTS) for reading the narration again.</summary>
    public List<StudioVoiceDto> Voices { get; set; } = new();
    public string[] Resolutions { get; set; } = Array.Empty<string>();
    public int MinClipSeconds { get; set; }
    public int MaxClipSeconds { get; set; }
    public AiStudioBudgetDto Budget { get; set; } = new();
    public int Projects { get; set; }
    public int Analyzing { get; set; }
    public int Generating { get; set; }
    public decimal SpentUsd { get; set; }
    public double StorageMb { get; set; }
}

// ───────────── projects ─────────────

public class AiStudioAudioTrackDto
{
    public string? Codec { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
}

public class AiStudioProjectSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string AspectRatio { get; set; } = string.Empty;
    public int ScenesTotal { get; set; }
    public int ProgressPercent { get; set; }
    public string? ProgressText { get; set; }
    public bool HasThumb { get; set; }
    public int ClipsDone { get; set; }
    public decimal CostUsd { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public class AiStudioFrameDto
{
    public double T { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? BlobId { get; set; }
}

public class AiStudioSceneDto
{
    public Guid Id { get; set; }
    public int Index { get; set; }
    public int Order { get; set; }
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public bool IsContinuation { get; set; }
    public bool Enabled { get; set; }
    public int TargetSeconds { get; set; }
    public bool HasThumb { get; set; }
    public List<AiStudioFrameDto> Frames { get; set; } = new();
    public JsonNode? Measured { get; set; }
    public JsonNode? Analysis { get; set; }
    public double Confidence { get; set; }
    public string? SummaryVi { get; set; }
    public string? Transcript { get; set; }
    public string? Narration { get; set; }
    public string? NarrationEn { get; set; }
    public bool NarrationEdited { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public int PromptVersion { get; set; }
    public bool PromptEdited { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    /// <summary>The latest generation of the scene (any status).</summary>
    public AiStudioGenerationDto? Latest { get; set; }
}

public class AiStudioProjectDto : AiStudioProjectSummaryDto
{
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceContainer { get; set; } = string.Empty;
    public long SourceSizeBytes { get; set; }
    public string FpsText { get; set; } = string.Empty;
    public double Fps { get; set; }
    public string? VideoCodec { get; set; }
    public long? BitrateKbps { get; set; }
    public bool HasAudio { get; set; }
    public List<AiStudioAudioTrackDto> AudioTracks { get; set; } = new();
    public bool HasSource { get; set; }
    public bool SourceDeleted { get; set; }

    public string AnalysisModel { get; set; } = "4b";
    public string? Stage { get; set; }
    public int ScenesDone { get; set; }
    public bool CancelRequested { get; set; }
    public string? WorkerMachine { get; set; }
    public DateTime? AnalysisStartedAtUtc { get; set; }
    public DateTime? AnalysisFinishedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
    public JsonNode? AnalysisMeta { get; set; }
    public JsonNode? Overview { get; set; }
    public JsonNode? Characters { get; set; }
    public JsonNode? Environments { get; set; }
    public JsonNode? Style { get; set; }
    public List<string> Palette { get; set; } = new();
    public JsonNode? Transcript { get; set; }

    public string PromptMode { get; set; } = "Faithful";
    public string Topic { get; set; } = string.Empty;
    /// <summary>"None" (silent), "Narration" (the narration read again by an AI voice) or "Original" (the reference's own sound).</summary>
    public string AudioMode { get; set; } = "None";
    public bool HasSourceAudio { get; set; }
    public string NarratorVoice { get; set; } = string.Empty;
    public string NarratorStyle { get; set; } = string.Empty;
    /// <summary>The whole narration read in one take: there is one / it matches the current words, voice and style.</summary>
    public bool HasNarrationTake { get; set; }
    public bool NarrationCurrent { get; set; }
    public double? NarrationSeconds { get; set; }
    /// <summary>How the reference speaker sounds, in Vietnamese ("giọng nam trầm, đọc chậm…"), or null.</summary>
    public string? SpeakerProfileVi { get; set; }
    public AiStudioVariationDto Variation { get; set; } = new();
    public string GlobalPrompt { get; set; } = string.Empty;
    public string NegativePrompt { get; set; } = string.Empty;

    public string VideoModel { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public string GenAspectRatio { get; set; } = string.Empty;
    public int? Seed { get; set; }
    public bool Continuity { get; set; }
    public bool HasReferenceImage { get; set; }

    public bool HasFinal { get; set; }
    public string? FinalStatus { get; set; }
    public string? FinalError { get; set; }
    public DateTime? FinalUpdatedAtUtc { get; set; }

    public List<AiStudioSceneDto> Scenes { get; set; } = new();
}

public class AiStudioProjectUpdateDto
{
    public string? Title { get; set; }
    public string? AnalysisModel { get; set; }
}

/// <summary>Creative-variation choices (mode B): what to change on purpose. Empty = keep the reference.</summary>
public class AiStudioVariationDto
{
    public string? Character { get; set; }
    public string? Wardrobe { get; set; }
    public string? Setting { get; set; }
    public string? Camera { get; set; }
    public string? Lighting { get; set; }
    public string? Style { get; set; }
}

public class AiStudioPromptSettingsDto
{
    public string Mode { get; set; } = "Faithful";
    /// <summary>Null = keep the project's theme.</summary>
    public string? Topic { get; set; }
    public AiStudioVariationDto Variation { get; set; } = new();
    public string? GlobalPrompt { get; set; }
    public string? NegativePrompt { get; set; }
    /// <summary>Rewrite the prompts the user edited by hand too.</summary>
    public bool OverwriteEdited { get; set; }
}

public class AiStudioBiblesDto
{
    public JsonNode? Characters { get; set; }
    public JsonNode? Environments { get; set; }
    public JsonNode? Style { get; set; }
    public bool OverwriteEdited { get; set; }
}

public class AiStudioScenePromptDto
{
    public string Text { get; set; } = string.Empty;
}

public class AiStudioSceneUpdateDto
{
    public bool? Enabled { get; set; }
    public int? TargetSeconds { get; set; }
}

public class AiStudioNarrationDto
{
    public string Text { get; set; } = string.Empty;
}

public class AiStudioNarratorDto
{
    /// <summary>Null = keep.</summary>
    public string? Voice { get; set; }
    public string? Style { get; set; }
    /// <summary>Pick the voice and style again from the reference speaker and the theme.</summary>
    public bool Suggest { get; set; }
}

public class AiStudioPromptVersionDto
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

// ───────────── generation ─────────────

public class AiStudioGeneratorSettingsDto
{
    public string VideoModel { get; set; } = string.Empty;
    public string Resolution { get; set; } = "720p";
    public string AspectRatio { get; set; } = "9:16";
    public int? Seed { get; set; }
    public bool Continuity { get; set; }
    /// <summary>Null = keep.</summary>
    public string? AudioMode { get; set; }
}

public class AiStudioGenerateRequestDto
{
    public List<Guid> SceneIds { get; set; } = new();
    /// <summary>A new random key per click (a resend with the same key does nothing).</summary>
    public string? RequestKey { get; set; }
    /// <summary>The cost the user saw and confirmed; refused when the real estimate is higher.</summary>
    public decimal ConfirmedCostUsd { get; set; }
}

public class AiStudioRetryRequestDto
{
    public string? RequestKey { get; set; }
    public decimal ConfirmedCostUsd { get; set; }
}

public class AiStudioEstimateDto
{
    public List<AiStudioEstimateLineDto> Lines { get; set; } = new();
    public decimal TotalUsd { get; set; }
    public int TotalSeconds { get; set; }
    public AiStudioBudgetDto Budget { get; set; } = new();
    public string? Blocker { get; set; }
}

public class AiStudioEstimateLineDto
{
    public Guid SceneId { get; set; }
    public int Order { get; set; }
    public int Seconds { get; set; }
    public decimal CostUsd { get; set; }
    public string? Skip { get; set; }
}

public class AiStudioGenerationDto
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string? ProjectTitle { get; set; }
    public Guid SceneId { get; set; }
    public int? SceneOrder { get; set; }
    public Guid BatchId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = string.Empty;
    public int Seconds { get; set; }
    public int? Seed { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string? ImageSource { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public decimal CostUsd { get; set; }
    public bool HasClip { get; set; }
    public bool Paid { get; set; }
    public string? ErrorMessage { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public bool FilesDeleted { get; set; }
}
