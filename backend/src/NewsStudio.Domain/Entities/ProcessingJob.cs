using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

public class ProcessingJob : BaseEntity
{
    public Guid VideoProjectId { get; set; }
    public VideoProject? VideoProject { get; set; }

    public string? HangfireJobId { get; set; }
    public string Step { get; set; } = "Pending";
    public string Status { get; set; } = "Pending"; // Pending, Running, Completed, Failed, Cancelled

    public int RetryCount { get; set; } = 0;
    public int MaxRetries { get; set; } = 3;

    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public int ProgressPercentage { get; set; } = 0;

    public string? IdempotencyKey { get; set; }
    public string? WorkerHost { get; set; }

    public string? ErrorMessage { get; set; }
    public string? ErrorDetails { get; set; }
}
