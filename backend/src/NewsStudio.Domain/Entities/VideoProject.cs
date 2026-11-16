using NewsStudio.Domain.Common;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Domain.Entities;

public class VideoProject : BaseEntity
{
    public Guid? ArticleId { get; set; }
    public Article? Article { get; set; }

    public string Title { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;

    public VideoProjectStatus Status { get; set; } = VideoProjectStatus.Pending;

    public int TargetDurationSeconds { get; set; } = 105; // upper bound; the AI shortens it for short articles
    public double? ActualDurationSeconds { get; set; }
    public string Resolution { get; set; } = "1080x1920";
    public string AspectRatio { get; set; } = "9:16";
    public int Fps { get; set; } = 30;

    /// <summary>AI editor chosen when the project was created ("Gemini" | "Claude").</summary>
    public string? AiProvider { get; set; }
    /// <summary>TTS voice chosen when the project was created, e.g. "vi-VN-HoaiMyNeural".</summary>
    public string? Voice { get; set; }
    /// <summary>TTS speaking-rate offset chosen when the project was created, e.g. "+12%".</summary>
    public string? VoiceSpeed { get; set; }

    /// <summary>
    /// Facebook Page the video is made for: its name is the brand shown on the video, and the video is
    /// posted there by default.
    /// </summary>
    public Guid? FacebookAccountId { get; set; }
    /// <summary>News topic of that page (see NewsTopics) — picks the video's colour theme.</summary>
    public string? Topic { get; set; }

    /// <summary>Latest AI editorial pass (JSON of EditorialPassDto): headline vs article check, caption, first comment.</summary>
    public string? EditorialPassJson { get; set; }

    public string? OutputVideoPath { get; set; }
    public string? ErrorMessage { get; set; }

    // Navigation
    public VideoScript? Script { get; set; }
    public ICollection<VideoScene> Scenes { get; set; } = new List<VideoScene>();
    public ICollection<VideoAsset> Assets { get; set; } = new List<VideoAsset>();
    public ICollection<ProcessingJob> ProcessingJobs { get; set; } = new List<ProcessingJob>();
    public ICollection<VideoReview> Reviews { get; set; } = new List<VideoReview>();
    public ICollection<FacebookPost> FacebookPosts { get; set; } = new List<FacebookPost>();
}
