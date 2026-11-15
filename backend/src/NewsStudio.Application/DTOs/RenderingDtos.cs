using NewsStudio.Domain.Enums;

namespace NewsStudio.Application.DTOs;

public class TtsSynthesisRequest
{
    public Guid ProjectId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Voice { get; set; } = "vi-VN-HoaiMyNeural";
    public string Speed { get; set; } = "+0%";
    public string Pitch { get; set; } = "+0Hz";
    public string Format { get; set; } = "audio/mpeg";
    public TtsProviderType Provider { get; set; } = TtsProviderType.EdgeTts;
}

public class SubtitleCueDto
{
    public int Index { get; set; }
    public double StartTimeSeconds { get; set; }
    public double EndTimeSeconds { get; set; }
    public string Text { get; set; } = string.Empty;
}

public class TtsSynthesisResult
{
    public string AudioFilePath { get; set; } = string.Empty;
    public double AudioDurationSeconds { get; set; }
    public string SubtitleFilePath { get; set; } = string.Empty;
    public string SubtitleVttFilePath { get; set; } = string.Empty;
    public List<SubtitleCueDto> Subtitles { get; set; } = new();
    public string Format { get; set; } = "audio/mpeg";
    public long FileSizeBytes { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
}

public class VideoRenderRequest
{
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string AudioFilePath { get; set; } = string.Empty;
    public string SubtitleFilePath { get; set; } = string.Empty;
    public string Resolution { get; set; } = "1080x1920";
    public string AspectRatio { get; set; } = "9:16";
    public int Fps { get; set; } = 30;
    public List<VideoScenePlanDto> Scenes { get; set; } = new();
    public bool EnableAudioDucking { get; set; } = true;
    public string BackgroundMusicTrack { get; set; } = "ambient-news";
}

public class VideoRenderResult
{
    public string OutputVideoPath { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public long FileSizeBytes { get; set; }
    public string Resolution { get; set; } = "1080x1920";
    public int Fps { get; set; } = 30;
    public string MimeType { get; set; } = "video/mp4";
    public string Sha256Hash { get; set; } = string.Empty;
}

public class VideoAssetDto
{
    public Guid Id { get; set; }
    public Guid VideoProjectId { get; set; }
    public string AssetType { get; set; } = string.Empty;
    public string LocalTempPath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public string? Sha256Hash { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsCleanedUp { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class RenderStatusResponseDto
{
    public Guid ProjectId { get; set; }
    public string Status { get; set; } = "Pending";
    public int ProgressPercentage { get; set; }
    public string? VideoUrl { get; set; }
    public string? AudioUrl { get; set; }
    public string? SubtitleUrl { get; set; }
    public double? DurationSeconds { get; set; }
    public string? ErrorMessage { get; set; }
}
