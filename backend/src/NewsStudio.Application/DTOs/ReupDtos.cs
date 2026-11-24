namespace NewsStudio.Application.DTOs;

// ───────────── Reup Video (free, on the owner's PC) ─────────────

public class ReupCharacterDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool ConsentConfirmed { get; set; }
    public string Status { get; set; } = "Checking";
    public string? RejectReason { get; set; }
    public double? FaceScore { get; set; }
    public bool HasThumb { get; set; }
    public int UsedByJobs { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CheckedAtUtc { get; set; }
}

public class ReupCharacterUpdateDto
{
    public string? Name { get; set; }
    public string? Notes { get; set; }
}

public class ReupBackdropDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "Image";
    public string Category { get; set; } = "Other";
    public string? Tags { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double? DurationSeconds { get; set; }
    public string SourceName { get; set; } = "Upload";
    public string? SourceUrl { get; set; }
    public string? Author { get; set; }
    public string LicenseName { get; set; } = string.Empty;
    public string? LicenseUrl { get; set; }
    public DateTime DownloadedAtUtc { get; set; }
    public bool HasThumb { get; set; }
    public int UseCount { get; set; }
}

/// <summary>Where a backdrop came from and its licence — required for every file added to the library.</summary>
public class ReupBackdropInfoDto
{
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? Tags { get; set; }
    public string? SourceName { get; set; }
    public string? SourceUrl { get; set; }
    public string? Author { get; set; }
    public string? LicenseName { get; set; }
    public string? LicenseUrl { get; set; }
}

/// <summary>Adds ONE file from a direct link (no bulk downloading).</summary>
public class ReupBackdropImportDto : ReupBackdropInfoDto
{
    public string Url { get; set; } = string.Empty;
}

/// <summary>A result of the Pexels / Pixabay search (free API keys), to add one at a time.</summary>
public class ReupStockItemDto
{
    public string Provider { get; set; } = string.Empty; // Pexels, Pixabay
    public string Kind { get; set; } = "Image";
    public string Id { get; set; } = string.Empty;
    public string PreviewUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
    public string? Author { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double? DurationSeconds { get; set; }
}

public class ReupStockImportDto
{
    public ReupStockItemDto Item { get; set; } = new();
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? Tags { get; set; }
}

public class ReupSegmentDto
{
    public double Start { get; set; }
    public Guid BackdropId { get; set; }
}

public class ReupPlacementDto
{
    /// <summary>Person size against the frame (1 = as filmed), anchored bottom-centre.</summary>
    public double Scale { get; set; } = 1.0;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    /// <summary>How much the person's brightness / tint follows the new scene (0–1).</summary>
    public double Harmonize { get; set; } = 0.3;
    /// <summary>Scene light wrapping the person's edges (0–1).</summary>
    public double LightWrap { get; set; } = 0.25;
    /// <summary>Background blur (depth of field), 0–10.</summary>
    public double Blur { get; set; } = 1.5;
    /// <summary>Cross-fade between scenes, seconds.</summary>
    public double Crossfade { get; set; } = 0.6;
    /// <summary>Slow zoom on picture backdrops so a still photo doesn't look frozen.</summary>
    public bool KenBurns { get; set; } = true;
}

public class ReupJobInputDto
{
    public string? Title { get; set; }
    public bool RightsConfirmed { get; set; }
    public bool PeopleConsentConfirmed { get; set; }
    public string? RightsNote { get; set; }
    public bool FaceSwapEnabled { get; set; }
    public Guid? CharacterId { get; set; }
    public bool BackgroundEnabled { get; set; }
    public List<ReupSegmentDto> Segments { get; set; } = new();
    public ReupPlacementDto Placement { get; set; } = new();
    public string AudioMode { get; set; } = "Remove";
    public double AudioVolume { get; set; } = 1.0;
    public double OriginalVolume { get; set; } = 1.0;
    public int OutputShortSide { get; set; } = 1080;
}

public class ReupQcCheckDto
{
    public string Name { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public string Detail { get; set; } = string.Empty;
}

public class ReupJobDto : ReupJobInputDto
{
    public Guid Id { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public long SourceSizeBytes { get; set; }
    public double SourceDurationSeconds { get; set; }
    public string SourceFps { get; set; } = string.Empty;
    public double SourceFpsValue { get; set; }
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public bool SourceHasAudio { get; set; }
    public bool HasSourceThumb { get; set; }
    public string? AudioFileName { get; set; }
    public int OutputWidth { get; set; }
    public int OutputHeight { get; set; }
    public string Status { get; set; } = "Draft";
    public bool PreviewRun { get; set; }
    public string? Stage { get; set; }
    public string? ProgressText { get; set; }
    public int ProgressPercent { get; set; }
    public int FramesDone { get; set; }
    public int FramesTotal { get; set; }
    public double? Fps { get; set; }
    public bool CancelRequested { get; set; }
    public string? WorkerMachine { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
    public bool HasPreview { get; set; }
    public bool HasOutput { get; set; }
    public double? OutputDurationSeconds { get; set; }
    public long? OutputSizeBytes { get; set; }
    public List<ReupQcCheckDto> Qc { get; set; } = new();
    public Dictionary<string, double>? Stats { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool FilesDeleted { get; set; }
    public string? CharacterName { get; set; }
}

public class ReupSettingsDto
{
    /// <summary>ONNX Runtime threads (0 = automatic).</summary>
    public int Threads { get; set; }
    /// <summary>The engine is stopped (job failed) when it uses more RAM than this.</summary>
    public int MaxRamMb { get; set; } = 8192;
    /// <summary>Folder for temporary files on the PC (empty = &lt;engine folder&gt;\work).</summary>
    public string? WorkDir { get; set; }
    public string ExecutionProvider { get; set; } = "cpu"; // cpu, directml
    public string Preset { get; set; } = "medium";
    public int Crf { get; set; } = 18;
    public int OutputRetentionDays { get; set; } = 7;
    public int BackdropQuotaMb { get; set; } = 3072;
    public string? PexelsApiKey { get; set; }
    public string? PixabayApiKey { get; set; }
    /// <summary>The owner holds a COMMERCIAL licence for models/licensed/swapper.onnx + identity.onnx.</summary>
    public bool SwapperLicenseConfirmed { get; set; }
    public string? SwapperLicenseNote { get; set; }
}

/// <summary>What the PC reported about its engine (written by the PC, read by the web page).</summary>
public class ReupEngineStatusDto
{
    public bool Ready { get; set; }
    public string? Error { get; set; }
    public DateTime? CheckedAtUtc { get; set; }
    public string? Machine { get; set; }
    public string? Home { get; set; }
    public string? Python { get; set; }
    public string? OnnxRuntime { get; set; }
    public string? OpenCv { get; set; }
    public List<string> Providers { get; set; } = new();
    public Dictionary<string, bool> Models { get; set; } = new();
    public Dictionary<string, double> BackgroundFps { get; set; } = new();
    public int CpuThreads { get; set; }
    public double RamGb { get; set; }
    public double? FreeDiskGb { get; set; }
    public string? FfmpegPath { get; set; }
}

public class ReupOverviewDto
{
    public bool PcOnline { get; set; }
    /// <summary>The PC is on but runs an older version than the cloud (updating): it takes jobs again once updated.</summary>
    public bool PcUpdating { get; set; }
    public string? PcMachine { get; set; }
    public ReupEngineStatusDto Engine { get; set; } = new();
    public bool FaceSwapAvailable { get; set; }
    public string? FaceSwapBlockedReason { get; set; }
    public int Done { get; set; }
    public int Running { get; set; }
    public int Queued { get; set; }
    public int Failed { get; set; }
    public int Characters { get; set; }
    public int Backdrops { get; set; }
    public double ProcessedMinutes { get; set; }
    public double StorageMb { get; set; }
    public double BackdropStorageMb { get; set; }
    public int BackdropQuotaMb { get; set; }
    public bool StockSearchPexels { get; set; }
    public bool StockSearchPixabay { get; set; }
    public List<ReupJobDto> Recent { get; set; } = new();
}
