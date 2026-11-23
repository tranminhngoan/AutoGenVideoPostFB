using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

/// <summary>
/// Binary content of the AI video studio (reference images, music, generated clips, final videos). Kept in the database
/// because the hosting disk is wiped on every deploy; rows are loaded only when the bytes are actually needed.
/// </summary>
public class StudioBlob : BaseEntity
{
    public string MimeType { get; set; } = "application/octet-stream";
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public long SizeBytes { get; set; }
}

/// <summary>Reusable reference image: a character, a location (e.g. Mekong countryside) or a prop/style.</summary>
public class StudioAsset : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "Character"; // Character, Location, Prop
    public string Description { get; set; } = string.Empty;
    public Guid BlobId { get; set; }
    public string MimeType { get; set; } = "image/jpeg";
    public long SizeBytes { get; set; }
}

/// <summary>Music library entry the planner can pick from.</summary>
public class StudioMusicTrack : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Mood { get; set; } = string.Empty; // free tags: "vui, quê hương, nhẹ nhàng"
    public Guid BlobId { get; set; }
    public string MimeType { get; set; } = "audio/mpeg";
    public long SizeBytes { get; set; }
    public double DurationSeconds { get; set; }
    public int UseCount { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
}

/// <summary>One video to be generated with Grok Imagine and posted to a Facebook Page.</summary>
public class StudioVideo : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public Guid? FacebookAccountId { get; set; }
    public string Script { get; set; } = string.Empty;
    public int TargetDurationSeconds { get; set; } = 60;
    public string AspectRatio { get; set; } = "9:16";
    public string Resolution { get; set; } = "720p";
    public string? StyleNotes { get; set; }
    public string AssetIdsJson { get; set; } = "[]"; // unused since text-to-video (kept for old rows)
    /// <summary>Video model id (Vercel AI Gateway), e.g. bytedance/seedance-v1.0-pro-fast.</summary>
    public string VideoModel { get; set; } = "bytedance/seedance-v1.0-pro-fast";
    /// <summary>
    /// Character / location bible written by the planner and editable by the user: fixed English looks reused in every
    /// shot prompt (text-to-video has no reference images) and each character's TTS voice. JSON.
    /// </summary>
    public string BibleJson { get; set; } = "{}";
    /// <summary>Same seed for every clip of the video, to keep faces and look as alike as text-to-video allows.</summary>
    public int Seed { get; set; }
    /// <summary>Add AI sound effects (MMAudio) matching each clip; their level in the mix.</summary>
    public bool SfxEnabled { get; set; } = true;
    public double SfxVolume { get; set; } = 0.7;

    public string MusicMode { get; set; } = "Auto"; // Auto, Manual, None
    public Guid? MusicTrackId { get; set; }
    public double MusicVolume { get; set; } = 0.15;

    // Draft, Planning, Planned, Generating, Composing, Ready, Publishing, Published, Failed
    public string Status { get; set; } = "Draft";
    public string? Progress { get; set; }
    public string? PlanNotes { get; set; }
    public string? Caption { get; set; }
    public Guid? FinalBlobId { get; set; }
    public double? FinalDurationSeconds { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public decimal ActualCostUsd { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PublishedPostId { get; set; }
    public DateTime? PublishedAtUtc { get; set; }

    public ICollection<StudioShot> Shots { get; set; } = new List<StudioShot>();
}

/// <summary>One generated clip (2–12 s) of a video, with its spoken line read by TTS.</summary>
public class StudioShot : BaseEntity
{
    public Guid StudioVideoId { get; set; }
    public StudioVideo? StudioVideo { get; set; }
    public int Index { get; set; }
    public int DurationSeconds { get; set; } = 8;
    public string Action { get; set; } = string.Empty;
    public string Camera { get; set; } = string.Empty;
    public string? Speaker { get; set; }
    public string? Dialogue { get; set; }
    public string? Sound { get; set; }
    public string AssetIdsJson { get; set; } = "[]";
    /// <summary>Start from the last frame of the previous shot's clip (continuous take) instead of the first asset image.</summary>
    public bool ContinueFromPrevious { get; set; }
    /// <summary>Opening frame built by the image model with every character of the shot in place (small JPEG, in the DB).</summary>
    public Guid? KeyframeBlobId { get; set; }
    /// <summary>Names of the bible characters visible in the shot (JSON array).</summary>
    public string CastJson { get; set; } = "[]";
    /// <summary>Name of the bible location the shot takes place in.</summary>
    public string? Location { get; set; }
    /// <summary>The spoken line read by TTS (small AAC in the database) and its length.</summary>
    public Guid? VoiceBlobId { get; set; }
    public double? VoiceSeconds { get; set; }
    /// <summary>Sound effects made from the clip by MMAudio (small AAC in the database).</summary>
    public Guid? SfxBlobId { get; set; }

    public string Status { get; set; } = "Pending"; // Pending, Generating, Done, Failed
    public Guid? ClipBlobId { get; set; }
    /// <summary>The provider's job (Vercel "operation" JSON) of the paid clip, to resume / recover it.</summary>
    public string? InteractionId { get; set; }
    public decimal CostUsd { get; set; }
    public int Attempts { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>Every paid call of the studio (planning text, video seconds), for the cost page and the monthly cap.</summary>
public class StudioCostRecord : BaseEntity
{
    public Guid? StudioVideoId { get; set; }
    public Guid? StudioShotId { get; set; }
    public string Kind { get; set; } = "Video"; // Plan, Video
    public string Model { get; set; } = string.Empty;
    public double? Seconds { get; set; }
    public long? Tokens { get; set; }
    public decimal CostUsd { get; set; }
    public string? Note { get; set; }
}
