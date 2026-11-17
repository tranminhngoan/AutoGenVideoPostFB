namespace NewsStudio.Application.DTOs;

/// <summary>Auto-post settings of one Facebook Page.</summary>
public class AutoPostSettingsDto
{
    public bool Enabled { get; set; }
    /// <summary>Topic key (GET /api/trending/topics): "tong-hop", "the-thao", … or "tu-chon".</summary>
    public string Topic { get; set; } = "tong-hop";
    /// <summary>Comma-separated keywords; required for "tu-chon", optional extra matches otherwise.</summary>
    public string? TopicKeywords { get; set; }
    public int PostsPerDay { get; set; } = 5;
    /// <summary>"HH:mm" in Vietnam time, one per post.</summary>
    public List<string> TimeSlots { get; set; } = new();
    /// <summary>"Mixed" | "VN" | "World"</summary>
    public string RegionMode { get; set; } = "Mixed";
    public string? Voice { get; set; }
    public string? VoiceSpeed { get; set; }
    public string? AiProvider { get; set; }
    public int TargetDurationSeconds { get; set; } = 105;
    public int LeadMinutes { get; set; } = 40;
    /// <summary>Daily "Điểm tin nóng" digest: one wall post with pictures + one Story.</summary>
    public bool DigestEnabled { get; set; } = true;
    /// <summary>"HH:mm" Vietnam time of the digest.</summary>
    public string DigestTime { get; set; } = "20:30";
    /// <summary>Number of stories in the digest (3–9).</summary>
    public int DigestItems { get; set; } = 6;
    /// <summary>Sundays: "Tuần qua có gì" (the week's biggest stories) instead of the daily digest.</summary>
    public bool WeeklyDigestEnabled { get; set; } = true;
    /// <summary>Post big breaking news right away, outside the time slots.</summary>
    public bool BreakingEnabled { get; set; } = true;
    /// <summary>Maximum breaking-news videos per day (0–5).</summary>
    public int BreakingPerDay { get; set; } = 2;
    /// <summary>Prefer the kinds of stories that got the most views on this page.</summary>
    public bool AutoTune { get; set; } = true;
    /// <summary>Also post every Reel on the Instagram account linked to the page.</summary>
    public bool InstagramEnabled { get; set; } = true;
    /// <summary>Writing tone of the page's scripts: neutral, youthful, expert, brief.</summary>
    public string WritingTone { get; set; } = "neutral";
    /// <summary>A logo was uploaded (read-only; upload with PUT /api/auto-post/pages/{id}/logo).</summary>
    public bool HasLogo { get; set; }
}

public class AutoPostRunDto
{
    public Guid Id { get; set; }
    public string SlotKey { get; set; } = string.Empty;
    public Guid? FacebookAccountId { get; set; }
    public string? PageName { get; set; }
    public DateTime ScheduledAtUtc { get; set; }
    /// <summary>"HH:mm" Vietnam time.</summary>
    public string SlotTime { get; set; } = string.Empty;
    /// <summary>"yyyy-MM-dd" Vietnam date.</summary>
    public string SlotDate { get; set; } = string.Empty;
    public bool IsManual { get; set; }
    /// <summary>Started outside the schedule because the story suddenly became big news.</summary>
    public bool IsBreaking { get; set; }
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

/// <summary>A connected Facebook Page with its schedule and today's progress.</summary>
public class AutoPostPageDto
{
    public Guid FacebookAccountId { get; set; }
    public string PageId { get; set; } = string.Empty;
    public string PageName { get; set; } = string.Empty;
    /// <summary>Connection status of the page token ("Active", "Expired", …).</summary>
    public string TokenStatus { get; set; } = "Active";
    public DateTime? TokenExpiresAtUtc { get; set; }
    /// <summary>Instagram account linked to the page (null = none found).</summary>
    public string? InstagramUsername { get; set; }
    public AutoPostSettingsDto Settings { get; set; } = new();
    public string TopicName { get; set; } = string.Empty;
    public List<AutoPostSlotDto> TodaySlots { get; set; } = new();
    public int PublishedToday { get; set; }
    public int FailedToday { get; set; }
    public DateTime? NextSlotUtc { get; set; }
    /// <summary>The run being produced / waiting / posting right now, if any.</summary>
    public AutoPostRunDto? CurrentRun { get; set; }
    public AutoPostRunDto? LastPublished { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class AutoPostTotalsDto
{
    public int Pages { get; set; }
    public int PagesEnabled { get; set; }
    /// <summary>Posts planned today across enabled pages.</summary>
    public int PlannedToday { get; set; }
    public int PublishedToday { get; set; }
    public int InProgress { get; set; }
    public int FailedToday { get; set; }
    public int PublishedLast7Days { get; set; }
}

public class AutoPostOverviewDto
{
    public string Today { get; set; } = string.Empty;
    public List<AutoPostPageDto> Pages { get; set; } = new();
    public AutoPostTotalsDto Totals { get; set; } = new();
    /// <summary>Last 7 days, every page, newest first.</summary>
    public List<AutoPostRunDto> RecentRuns { get; set; } = new();
    /// <summary>How many videos are made at the same time across all pages.</summary>
    public int MaxConcurrentVideos { get; set; } = 1;
    public List<string> Warnings { get; set; } = new();
}
