using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

/// <summary>
/// "Reup Video" (free, on the owner's PC): an AI character photo whose face can replace the person's face in a video.
/// The photo is never altered; its face is checked on the PC (exactly one clear, frontal face) before it can be used.
/// Files live in the shared bucket (StudioBlobs rows + StudioFileStore), never in the database.
/// </summary>
public class ReupCharacter : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid ImageBlobId { get; set; }
    public Guid? ThumbBlobId { get; set; }
    /// <summary>The owner confirms the face is AI-made or that the real person agreed to its use.</summary>
    public bool ConsentConfirmed { get; set; }
    /// <summary>Checking (waiting for the PC), Ready, Rejected.</summary>
    public string Status { get; set; } = "Checking";
    public string? RejectReason { get; set; }
    public double? FaceScore { get; set; }
    public string? FaceBoxJson { get; set; }
    /// <summary>SFace feature of the face (128 numbers, JSON) — identifies the character, e.g. to spot duplicates.</summary>
    public string? EmbeddingJson { get; set; }
    public DateTime? CheckedAtUtc { get; set; }
}

/// <summary>A new scene for videos: a picture or a video, with where it came from and under which licence.</summary>
public class ReupBackdrop : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "Image"; // Image, Video
    public string Category { get; set; } = "Other";
    public string? Tags { get; set; }
    public Guid BlobId { get; set; }
    public Guid? ThumbBlobId { get; set; }
    public string MimeType { get; set; } = "image/jpeg";
    public long SizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double? DurationSeconds { get; set; }
    /// <summary>Pexels, Pixabay, Upload (the owner's own file) or Other.</summary>
    public string SourceName { get; set; } = "Upload";
    public string? SourceUrl { get; set; }
    public string? Author { get; set; }
    public string LicenseName { get; set; } = string.Empty;
    public string? LicenseUrl { get; set; }
    public DateTime DownloadedAtUtc { get; set; } = DateTime.UtcNow;
    public int UseCount { get; set; }
}

/// <summary>One source video processed on the PC: face swap and/or new background, audio choice, H.264 MP4 out.</summary>
public class ReupJob : BaseEntity
{
    public string Title { get; set; } = string.Empty;

    // source
    public Guid SourceBlobId { get; set; }
    public Guid? SourceThumbBlobId { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public long SourceSizeBytes { get; set; }
    public double SourceDurationSeconds { get; set; }
    /// <summary>Frame rate as FFmpeg writes it (e.g. "30000/1001"), kept exactly in the output.</summary>
    public string SourceFps { get; set; } = "30/1";
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public bool SourceHasAudio { get; set; }

    // the owner's statements (required before processing)
    public bool RightsConfirmed { get; set; }
    public bool PeopleConsentConfirmed { get; set; }
    public string? RightsNote { get; set; }

    // what to do
    public bool FaceSwapEnabled { get; set; }
    public Guid? CharacterId { get; set; }
    public bool BackgroundEnabled { get; set; }
    /// <summary>[{ "start": 0, "backdropId": "…" }, …] — one entry = one scene for the whole video.</summary>
    public string SegmentsJson { get; set; } = "[]";
    /// <summary>Person size / position and blending: { scale, offsetX, offsetY, harmonize, lightWrap, blur, crossfade }.</summary>
    public string PlacementJson { get; set; } = "{}";
    public string AudioMode { get; set; } = "Remove"; // Remove, KeepOriginal, Replace, Mix
    public Guid? AudioBlobId { get; set; }
    public string? AudioFileName { get; set; }
    public double AudioVolume { get; set; } = 1.0;
    public double OriginalVolume { get; set; } = 1.0;
    public int OutputShortSide { get; set; } = 1080; // 720 or 1080

    // run
    /// <summary>Draft, Queued, Processing, Done, Failed, Cancelled.</summary>
    public string Status { get; set; } = "Draft";
    public bool PreviewRun { get; set; }
    public string? Stage { get; set; }
    public string? ProgressText { get; set; }
    public int ProgressPercent { get; set; }
    public int FramesDone { get; set; }
    public int FramesTotal { get; set; }
    public double? Fps { get; set; }
    public bool CancelRequested { get; set; }
    public string? HangfireJobId { get; set; }
    public string? WorkerMachine { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }

    // results
    public Guid? PreviewBlobId { get; set; }
    public Guid? OutputBlobId { get; set; }
    public double? OutputDurationSeconds { get; set; }
    public long? OutputSizeBytes { get; set; }
    public int? OutputWidth { get; set; }
    public int? OutputHeight { get; set; }
    /// <summary>Automatic checks of the output (duration, FPS, size, audio, decodes, black frames) as JSON.</summary>
    public string? QcReportJson { get; set; }
    /// <summary>Frames swapped / kept original, speed… from the engine (JSON).</summary>
    public string? StatsJson { get; set; }
    /// <summary>When the source and output files are deleted from the bucket (keeps the free 10 GB from filling up).</summary>
    public DateTime? ExpiresAtUtc { get; set; }
    public bool FilesDeleted { get; set; }
}
