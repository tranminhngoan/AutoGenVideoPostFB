using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

public class VideoMetric : BaseEntity
{
    public Guid FacebookPostId { get; set; }
    public FacebookPost? FacebookPost { get; set; }

    public long Views { get; set; } = 0;
    public long Reach { get; set; } = 0;
    public long Engagement { get; set; } = 0;
    public long Shares { get; set; } = 0;
    public long Comments { get; set; } = 0;
    public double WatchTimeSeconds { get; set; } = 0;

    public DateTime CollectedAtUtc { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Recorded";
    public string? ErrorMessage { get; set; }
}

public class SystemSetting : BaseEntity
{
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
    public string Group { get; set; } = "General"; // AI, TTS, Storage, Facebook, Hangfire
    public bool IsEncrypted { get; set; } = false;

    public string Status { get; set; } = "Active";
    public string? ErrorMessage { get; set; }
}

public class AuditLog : BaseEntity
{
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // Created, Updated, Reviewed, Published, Cancelled, CleanedUp
    public string PerformedBy { get; set; } = "System";
    public string? DetailsJson { get; set; }

    public string Status { get; set; } = "Success";
    public string? ErrorMessage { get; set; }
}
