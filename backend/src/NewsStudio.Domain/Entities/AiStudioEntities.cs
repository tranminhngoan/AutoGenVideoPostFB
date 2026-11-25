using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

/// <summary>
/// AI Video Studio: a reference video analysed on the owner's PC (local AI, no paid analysis), turned into per-scene
/// prompts and regenerated as new clips with Seedance. The app has a single owner (no tenants): access to writes is the
/// studio key, like the rest of the studio.
/// </summary>
public class AiStudioProject : BaseEntity
{
    public string Title { get; set; } = string.Empty;

    // Draft (uploaded), Queued, Analyzing, Analyzed, Failed, Cancelled
    public string Status { get; set; } = "Draft";

    // ── reference video ──
    public Guid? SourceBlobId { get; set; }
    public Guid? SourceThumbBlobId { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceContainer { get; set; } = "mp4"; // mp4, mov, webm
    public long SourceSizeBytes { get; set; }
    public double DurationSeconds { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string FpsText { get; set; } = "30/1";
    public double Fps { get; set; }
    public string? VideoCodec { get; set; }
    public long? BitrateKbps { get; set; }
    public string AspectRatio { get; set; } = "9:16";
    public bool HasAudio { get; set; }
    /// <summary>Audio tracks: [{codec, sampleRate, channels}] (JSON).</summary>
    public string AudioJson { get; set; } = "[]";
    /// <summary>The reference video was removed from storage after the retention period (analysis kept).</summary>
    public bool SourceDeleted { get; set; }

    // ── analysis (on the PC) ──
    /// <summary>Local model: "4b" (balanced, GPU) or "8b" (more accurate, much slower).</summary>
    public string AnalysisModel { get; set; } = "4b";
    public string? Stage { get; set; }
    public string? ProgressText { get; set; }
    public int ProgressPercent { get; set; }
    public int ScenesDone { get; set; }
    public int ScenesTotal { get; set; }
    public bool CancelRequested { get; set; }
    public string? HangfireJobId { get; set; }
    public string? WorkerMachine { get; set; }
    public DateTime? AnalysisStartedAtUtc { get; set; }
    public DateTime? AnalysisFinishedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>Engine, model, backend, timings of the last analysis (JSON).</summary>
    public string AnalysisMetaJson { get; set; } = "{}";
    /// <summary>{summary, summary_vi, scene_relations} (JSON).</summary>
    public string OverviewJson { get; set; } = "{}";
    /// <summary>Bibles written by the analysis, editable by the user (JSON arrays / object).</summary>
    public string CharacterBibleJson { get; set; } = "[]";
    public string EnvironmentBibleJson { get; set; } = "[]";
    public string StyleBibleJson { get; set; } = "{}";
    /// <summary>Colours measured on the frames (hex list, JSON).</summary>
    public string PaletteJson { get; set; } = "[]";
    /// <summary>Speech transcript if any ({language, segments}, JSON).</summary>
    public string? TranscriptJson { get; set; }

    // ── prompt engine ──
    /// <summary>Faithful (A), Creative (B), Cinematic (C).</summary>
    public string PromptMode { get; set; } = "Faithful";
    /// <summary>The channel theme added to every prompt after the analysis ("asian-beauty", "vn-childhood"; empty = none).</summary>
    public string Topic { get; set; } = string.Empty;
    /// <summary>Creative-variation choices: character, wardrobe, setting, camera, lighting, style (JSON).</summary>
    public string VariationJson { get; set; } = "{}";
    public string GlobalPrompt { get; set; } = string.Empty;
    public string NegativePrompt { get; set; } = string.Empty;

    // ── generator ──
    public string VideoModel { get; set; } = "bytedance/seedance-v1.0-pro-fast";
    public string Resolution { get; set; } = "720p";
    public string GenAspectRatio { get; set; } = "9:16";
    /// <summary>Same seed for every clip (null = random per clip).</summary>
    public int? Seed { get; set; }
    /// <summary>Start each clip from the last frame of the previous scene's clip (image-to-video, when the model accepts it).</summary>
    public bool Continuity { get; set; }
    /// <summary>The user's own image used as the first frame of the first scene (optional).</summary>
    public Guid? ReferenceImageBlobId { get; set; }

    /// <summary>All finished scene clips joined in order (optional).</summary>
    public Guid? FinalBlobId { get; set; }
    public string? FinalStatus { get; set; } // null, Assembling, Done, Failed
    public string? FinalError { get; set; }
    public DateTime? FinalUpdatedAtUtc { get; set; }

    public ICollection<AiStudioScene> Scenes { get; set; } = new List<AiStudioScene>();
}

/// <summary>One scene of the reference video (a shot, or part of a shot longer than one clip).</summary>
public class AiStudioScene : BaseEntity
{
    public Guid ProjectId { get; set; }
    public AiStudioProject? Project { get; set; }
    /// <summary>Position in the reference video (1…).</summary>
    public int Index { get; set; }
    /// <summary>Position in the new video (the user may reorder).</summary>
    public int Order { get; set; }
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    /// <summary>Continues the previous scene without a cut (a long shot split into clips).</summary>
    public bool IsContinuation { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>Length of the clip to generate (2–12 s).</summary>
    public int TargetSeconds { get; set; } = 5;
    public Guid? ThumbBlobId { get; set; }
    /// <summary>Keyframes: [{t, reason, blobId}] (JSON).</summary>
    public string FramesJson { get; set; } = "[]";
    /// <summary>Measured without AI: camera movement, motion level, brightness, colours (JSON).</summary>
    public string MeasuredJson { get; set; } = "{}";
    /// <summary>The local vision model's structured description (JSON).</summary>
    public string AnalysisJson { get; set; } = "{}";
    public double Confidence { get; set; }
    public string? SummaryVi { get; set; }
    public string? Transcript { get; set; }

    /// <summary>The prompt sent to Seedance (latest version).</summary>
    public string Prompt { get; set; } = string.Empty;
    public int PromptVersion { get; set; }
    /// <summary>The user wrote this prompt by hand: the engine does not overwrite it when settings change.</summary>
    public bool PromptEdited { get; set; }
}

/// <summary>Every saved version of a scene prompt or of the global prompt, to restore an earlier one.</summary>
public class AiStudioPromptVersion : BaseEntity
{
    public Guid ProjectId { get; set; }
    /// <summary>Null = the project's global prompt.</summary>
    public Guid? SceneId { get; set; }
    public int Version { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Source { get; set; } = "engine"; // engine, user, restore
    public string Mode { get; set; } = "Faithful";
}

/// <summary>One Seedance clip generated for a scene (a scene can have many: retries, other settings).</summary>
public class AiStudioGeneration : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid SceneId { get; set; }
    public Guid BatchId { get; set; }
    /// <summary>Client key of the "Generate" click, so a double click / resend does not create (and pay) twice.</summary>
    public string? RequestKey { get; set; }
    // Queued, Generating, Done, Failed, Cancelled
    public string Status { get; set; } = "Queued";
    public string Model { get; set; } = string.Empty;
    public string Resolution { get; set; } = "720p";
    public string AspectRatio { get; set; } = "9:16";
    public int Seconds { get; set; }
    public int? Seed { get; set; }
    public string Prompt { get; set; } = string.Empty;
    /// <summary>"previous-clip", "reference-image" or null (text only).</summary>
    public string? ImageSource { get; set; }
    public string ParamsJson { get; set; } = "{}";
    /// <summary>The provider's paid job (Vercel "operation"), kept to pick it up again without paying twice.</summary>
    public string? ProviderOperation { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public decimal CostUsd { get; set; }
    public Guid? ClipBlobId { get; set; }
    public string? ErrorMessage { get; set; }
    public int Attempts { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public bool FilesDeleted { get; set; }
}
