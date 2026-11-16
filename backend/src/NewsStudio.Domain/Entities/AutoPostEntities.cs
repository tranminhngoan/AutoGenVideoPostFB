using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

/// <summary>
/// "Tự động đăng" settings (a single row). Every day, at each time slot (Vietnam time), the hottest unused
/// story is turned into a video and posted to the Facebook Page without manual steps.
/// </summary>
public class AutoPostSettings : BaseEntity
{
    public bool Enabled { get; set; }

    /// <summary>Maximum automatic posts per day (= number of time slots).</summary>
    public int PostsPerDay { get; set; } = 5;

    /// <summary>Comma-separated "HH:mm" posting times in Vietnam time (UTC+7), e.g. "07:00,11:30,17:30,20:00,21:30".</summary>
    public string TimeSlots { get; set; } = string.Empty;

    /// <summary>"Mixed" (alternate Vietnam / international), "VN" or "World".</summary>
    public string RegionMode { get; set; } = "Mixed";

    /// <summary>Null = server default voice.</summary>
    public string? Voice { get; set; }
    public string? VoiceSpeed { get; set; }
    public string? AiProvider { get; set; }
    public int TargetDurationSeconds { get; set; } = 105;

    /// <summary>Page to post to; null = the first connected page.</summary>
    public Guid? FacebookAccountId { get; set; }

    /// <summary>How long before a slot the video production starts, so it is ready on time.</summary>
    public int LeadMinutes { get; set; } = 40;
}

/// <summary>One automatic post: a time slot, the story chosen for it, its video and the Facebook result.</summary>
public class AutoPostRun : BaseEntity
{
    /// <summary>"2026-11-16 07:00" for a scheduled slot, "2026-11-16 now-0915" for "Đăng ngay". Unique.</summary>
    public string SlotKey { get; set; } = string.Empty;
    public DateTime ScheduledAtUtc { get; set; }
    public bool IsManual { get; set; }

    /// <summary>Producing → WaitingSlot → Publishing → Published, or Failed / Skipped.</summary>
    public string Status { get; set; } = "Producing";
    public int Attempts { get; set; }

    public Guid? TrendingArticleId { get; set; }
    public Guid? ClusterId { get; set; }
    public string? ArticleUrl { get; set; }
    public string? ArticleTitle { get; set; }
    public string? SourceName { get; set; }
    /// <summary>"VN" or "World".</summary>
    public string? Region { get; set; }
    public double? HotScore { get; set; }

    public Guid? VideoProjectId { get; set; }
    public string? FacebookPostUrl { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }

    /// <summary>Latest progress / error note in Vietnamese, shown in the UI.</summary>
    public string? Message { get; set; }
}
