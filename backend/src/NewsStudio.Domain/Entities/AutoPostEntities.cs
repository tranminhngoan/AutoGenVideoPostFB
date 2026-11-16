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

    /// <summary>Post the daily "Điểm tin nóng" digest (one wall post with pictures + one Story) of this page's topic.</summary>
    public bool DigestEnabled { get; set; } = true;

    /// <summary>"HH:mm" Vietnam time of the daily digest.</summary>
    public string DigestTime { get; set; } = "20:30";

    /// <summary>How many stories the digest summarises.</summary>
    public int DigestItems { get; set; } = 6;

    /// <summary>On Sundays the evening digest is "Tuần qua có gì" (the week's biggest stories) instead.</summary>
    public bool WeeklyDigestEnabled { get; set; } = true;

    /// <summary>Post a story right away (outside the time slots) when it suddenly becomes big news.</summary>
    public bool BreakingEnabled { get; set; } = true;

    /// <summary>At most this many breaking-news videos per day.</summary>
    public int BreakingPerDay { get; set; } = 2;

    /// <summary>Learn from this page's views: prefer the kinds of stories that got the most views.</summary>
    public bool AutoTune { get; set; } = true;

    /// <summary>Also post every Reel on the Instagram account linked to the page.</summary>
    public bool InstagramEnabled { get; set; } = true;
}

/// <summary>
/// One daily "Điểm tin nóng" digest of a page: the day's most striking stories of its topic, each as a
/// picture with a short summary written on it, posted as one multi-picture wall post and one Story.
/// </summary>
public class DailyDigestRun : BaseEntity
{
    public Guid? FacebookAccountId { get; set; }

    /// <summary>"yyyy-MM-dd" Vietnam date.</summary>
    public string DigestDate { get; set; } = string.Empty;

    /// <summary>"Daily" or "Weekly" (Sunday: "Tuần qua có gì").</summary>
    public string Kind { get; set; } = "Daily";

    /// <summary>A preview made from the app ("Tạo thử"): pictures only, nothing posted.</summary>
    public bool IsPreview { get; set; }

    /// <summary>Working → Published / Partial (one of post or Story failed) / Failed, or Preview.</summary>
    public string Status { get; set; } = "Working";

    /// <summary>The chosen stories (JSON list of DailyDigestItemDto).</summary>
    public string? ItemsJson { get; set; }

    /// <summary>Text of the wall post.</summary>
    public string? Caption { get; set; }

    /// <summary>Folder holding the pictures and the Story video (temporary storage).</summary>
    public string? WorkFolder { get; set; }

    public string? PostId { get; set; }
    public string? PostError { get; set; }
    public string? StoryPostId { get; set; }
    public string? StoryError { get; set; }

    public DateTime? StartedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }

    /// <summary>Latest progress / error note in Vietnamese.</summary>
    public string? Message { get; set; }
}

/// <summary>One automatic post: a time slot, the story chosen for it, its video and the Facebook result.</summary>
public class AutoPostRun : BaseEntity
{
    /// <summary>"2026-11-16 07:00 {pageId}" for a scheduled slot, "2026-11-16 now-091530 {pageId}" for "Đăng ngay". Unique.</summary>
    public string SlotKey { get; set; } = string.Empty;
    public DateTime ScheduledAtUtc { get; set; }
    public bool IsManual { get; set; }

    /// <summary>Started outside the schedule because the story suddenly became big news.</summary>
    public bool IsBreaking { get; set; }

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
