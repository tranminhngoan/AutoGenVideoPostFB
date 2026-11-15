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

    public int TargetDurationSeconds { get; set; } = 50;
    public double? ActualDurationSeconds { get; set; }
    public string Resolution { get; set; } = "1080x1920";
    public string AspectRatio { get; set; } = "9:16";
    public int Fps { get; set; } = 30;

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
