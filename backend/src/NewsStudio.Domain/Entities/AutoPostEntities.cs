using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

/// <summary>
/// "Tự động đăng" settings of ONE Facebook Page. Every day, at each time slot (Vietnam time), the hottest
/// unused story of the page's topic is turned into a video and posted to that page (with its own token).
/// </summary>
public class AutoPostSettings : BaseEntity
{
    /// <summary>The page these settings belong to (one row per page).</summary>
    public Guid? FacebookAccountId { get; set; }

    public bool Enabled { get; set; }

    /// <summary>Topic key (see NewsTopics): "tong-hop", "the-thao", "cong-nghe", … or "tu-chon".</summary>
    public string Topic { get; set; } = "tong-hop";

    /// <summary>Comma-separated extra keywords; the only filter for the "tu-chon" topic.</summary>
    public string? TopicKeywords { get; set; }

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

    /// <summary>How long before a slot the video production starts, so it is ready on time.</summary>
    public int LeadMinutes { get; set; } = 40;
}

/// <summary>One automatic post: a time slot, the story chosen for it, its video and the Facebook result.</summary>
public class AutoPostRun : BaseEntity
{
    /// <summary>"2026-11-16 07:00 {pageId}" for a scheduled slot, "2026-11-16 now-091530 {pageId}" for "Đăng ngay". Unique.</summary>
    public string SlotKey { get; set; } = string.Empty;
    public DateTime ScheduledAtUtc { get; set; }
    public bool IsManual { get; set; }

    /// <summary>Page the video is posted to.</summary>
    public Guid? FacebookAccountId { get; set; }

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
