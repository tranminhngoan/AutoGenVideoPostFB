namespace NewsStudio.Application.DTOs;

public class AutoPostSettingsDto
{
    public bool Enabled { get; set; }
    public int PostsPerDay { get; set; } = 5;
    /// <summary>"HH:mm" in Vietnam time, one per post.</summary>
    public List<string> TimeSlots { get; set; } = new();
    /// <summary>"Mixed" | "VN" | "World"</summary>
    public string RegionMode { get; set; } = "Mixed";
    public string? Voice { get; set; }
    public string? VoiceSpeed { get; set; }
    public string? AiProvider { get; set; }
    public int TargetDurationSeconds { get; set; } = 105;
    public Guid? FacebookAccountId { get; set; }
    public int LeadMinutes { get; set; } = 40;
}

public class AutoPostRunDto
{
    public Guid Id { get; set; }
    public string SlotKey { get; set; } = string.Empty;
    public DateTime ScheduledAtUtc { get; set; }
    /// <summary>"HH:mm" Vietnam time.</summary>
    public string SlotTime { get; set; } = string.Empty;
    /// <summary>"yyyy-MM-dd" Vietnam date.</summary>
    public string SlotDate { get; set; } = string.Empty;
    public bool IsManual { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public string? ArticleUrl { get; set; }
    public string? ArticleTitle { get; set; }
    public string? SourceName { get; set; }
    public string? Region { get; set; }
    public double? HotScore { get; set; }
    public Guid? VideoProjectId { get; set; }
    /// <summary>Video pipeline step (Extracting, Scripting, Rendering…) while producing.</summary>
    public string? ProjectStatus { get; set; }
    public int? ProgressPercentage { get; set; }
    public string? FacebookPostUrl { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public string? Message { get; set; }
}

/// <summary>One planned slot of today, with its run if it has started.</summary>
public class AutoPostSlotDto
{
    public string Time { get; set; } = string.Empty;
    public DateTime ScheduledAtUtc { get; set; }
    /// <summary>Region the slot will pick from: "VN" or "World".</summary>
    public string PlannedRegion { get; set; } = "VN";
    /// <summary>"Upcoming" | "Missed" | run status.</summary>
    public string State { get; set; } = "Upcoming";
    public AutoPostRunDto? Run { get; set; }
}

public class AutoPostOverviewDto
{
    public AutoPostSettingsDto Settings { get; set; } = new();
    public string Today { get; set; } = string.Empty;
    public List<AutoPostSlotDto> TodaySlots { get; set; } = new();
    public List<AutoPostRunDto> RecentRuns { get; set; } = new();
    public int PublishedToday { get; set; }
    public DateTime? NextSlotUtc { get; set; }
    public bool FacebookConnected { get; set; }
    public List<string> Warnings { get; set; } = new();
}
