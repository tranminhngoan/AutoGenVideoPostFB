using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

/// <summary>
/// "Chiến dịch tăng trưởng" (Meta Growth Campaign): a time-boxed plan (default the 7-day Meta One trial) to grow one
/// connected Facebook Page. The app has a single owner (no tenants); data is kept apart per Page and per campaign, and
/// every child row is checked against the campaign in the URL. The Page itself comes from the existing connection
/// (<see cref="FacebookAccount"/>) — nothing here stores a token.
/// </summary>
public class GrowthCampaign : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The connected Page (null when the Page was later removed from the app: the campaign history stays).</summary>
    public Guid? FacebookAccountId { get; set; }
    public FacebookAccount? FacebookAccount { get; set; }
    /// <summary>Page name when the campaign was made (shown when the Page was removed).</summary>
    public string PageName { get; set; } = string.Empty;

    /// <summary>Draft (chưa bắt đầu), Running, Paused, Completed, Cancelled.</summary>
    public string Status { get; set; } = "Draft";

    /// <summary>First and last day of the campaign (Vietnam dates, inclusive).</summary>
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }

    public long? TargetFollowers { get; set; }
    public long? TargetViews { get; set; }
    public int ReelsPerDay { get; set; } = 1;
    public int PhotosPerDay { get; set; } = 2;
    public int StoriesPerDay { get; set; } = 3;
    /// <summary>Posting times of the day, Vietnam time ("07:30,11:30,20:00").</summary>
    public string TimeSlots { get; set; } = "";
    /// <summary>AI spend allowed for this campaign (captions, analysis), USD.</summary>
    public decimal AiBudgetUsd { get; set; } = 1m;
    public decimal AiSpentUsd { get; set; }
    /// <summary>Auto (scheduled content is posted at its time) or Review (each item needs approval first).</summary>
    public string ApprovalMode { get; set; } = "Review";
    /// <summary>Everything posted on the Facebook Page is also posted on its linked Instagram account.</summary>
    public bool AlsoInstagram { get; set; } = true;

    /// <summary>Followers read from Meta when the campaign started / ended (null = Meta didn't give the number).</summary>
    public long? StartingFollowers { get; set; }
    public DateTime? StartingFollowersAtUtc { get; set; }
    public long? EndingFollowers { get; set; }
    public DateTime? EndingFollowersAtUtc { get; set; }

    /// <summary>Price paid for Meta One for the campaign period (entered by the owner), and its currency.</summary>
    public decimal? MetaOneCost { get; set; }
    public string MetaOneCurrency { get; set; } = "VND";

    /// <summary>Meta One checklist: {key: {done, note, atUtc}} (JSON).</summary>
    public string ChecklistJson { get; set; } = "{}";
    /// <summary>Day-by-day plan (theme / goal of each day), editable (JSON array).</summary>
    public string PlanJson { get; set; } = "[]";
    /// <summary>Which Meta metrics this Page answered for, and why the others are missing: {metric: {ok, error, atUtc}} (JSON).</summary>
    public string MetricAvailabilityJson { get; set; } = "{}";
    public DateTime? LastSyncedAtUtc { get; set; }
    public string? LastSyncError { get; set; }
    public string? Notes { get; set; }

    public ICollection<GrowthScheduledContent> Contents { get; set; } = new List<GrowthScheduledContent>();
}

/// <summary>One piece of content in the campaign calendar.</summary>
public class GrowthScheduledContent : BaseEntity
{
    public Guid CampaignId { get; set; }
    public GrowthCampaign? Campaign { get; set; }

    /// <summary>Reel, Photo (one picture), Album (several pictures), Story, Interactive (question / discussion post).</summary>
    public string Kind { get; set; } = "Photo";
    /// <summary>Draft, PendingApproval, Scheduled, Publishing, Published, Failed, Cancelled.</summary>
    public string Status { get; set; } = "Draft";

    public string Title { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string Hashtags { get; set; } = string.Empty;
    /// <summary>Caption style used (Emotional, Nostalgic, Story, Curiosity, Discussion, Knowledge, Short) — for analysis.</summary>
    public string? Style { get; set; }
    /// <summary>Call to action / question for the comments — for analysis.</summary>
    public string? Cta { get; set; }
    public string? Topic { get; set; }

    /// <summary>Video source of a Reel / video Story: StudioVideo, AiStudioProject, VideoProject (or null for uploads).</summary>
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    /// <summary>Uploaded / template pictures or a video (ids of <see cref="GrowthContentAsset"/>, in order, JSON).</summary>
    public string AssetIdsJson { get; set; } = "[]";

    public DateTime? ScheduledAtUtc { get; set; }
    /// <summary>Client-chosen key: the same create request twice makes one item.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
    /// <summary>Post it by hand (Story when the API isn't available to this Page): the app only reminds and records.</summary>
    public bool ManualPublish { get; set; }

    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? PublishStartedAtUtc { get; set; }
    public string? LastError { get; set; }
    /// <summary>Kind of the last error: Transient, Token, Permission, Unsupported, Content, Unknown.</summary>
    public string? ErrorKind { get; set; }

    public DateTime? PublishedAtUtc { get; set; }
    public string? ExternalPostId { get; set; }
    public string? ExternalVideoId { get; set; }
    public string? PermalinkUrl { get; set; }
    /// <summary>Story cut from a longer video: where the highlight starts and how long it is (seconds).</summary>
    public double? ClipStartSeconds { get; set; }
    public double? ClipSeconds { get; set; }
    /// <summary>The drama script the post was made from (autopilot), so it is never used twice on the Page.</summary>
    public Guid? ScriptId { get; set; }

    /// <summary>The <see cref="FacebookPost"/> record (Reels) the existing stats already follow.</summary>
    public Guid? FacebookPostRecordId { get; set; }
    /// <summary>The same content on the Page's linked Instagram (a failure there never undoes the Facebook post).</summary>
    public string? InstagramMediaId { get; set; }
    public string? InstagramPermalink { get; set; }
    public string? InstagramError { get; set; }

    /// <summary>Posted by hand (marked by the owner) instead of by the app.</summary>
    public bool PublishedManually { get; set; }

    public ICollection<GrowthPostMetric> Metrics { get; set; } = new List<GrowthPostMetric>();
}

/// <summary>A picture or video uploaded for the campaign, or exported from an image template. The file is in the studio store.</summary>
public class GrowthContentAsset : BaseEntity
{
    public Guid CampaignId { get; set; }
    public GrowthCampaign? Campaign { get; set; }
    public string Kind { get; set; } = "Image"; // Image, Video
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = "image/jpeg";
    public long SizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double? DurationSeconds { get; set; }
    /// <summary>Upload or Template.</summary>
    public string Source { get; set; } = "Upload";
    /// <summary>Id of the file in the studio store (StudioFileStore), never served by any other route.</summary>
    public Guid FileId { get; set; }
}

/// <summary>
/// Page numbers of one Vietnam day, as Meta reported them (null = not available for this Page) plus numbers the owner
/// typed in (Meta One invitations, page visits seen in the app) — kept apart so the two are never mixed up.
/// </summary>
public class GrowthDailyMetric : BaseEntity
{
    public Guid FacebookAccountId { get; set; }
    public DateOnly Date { get; set; }

    public long? Followers { get; set; }
    public long? NewFollows { get; set; }
    public long? Unfollows { get; set; }
    public long? PageViews { get; set; }
    public long? MediaViews { get; set; }
    public long? MediaViewers { get; set; }
    public long? Engagements { get; set; }
    public DateTime? CollectedAtUtc { get; set; }

    // typed in by the owner
    public long? ManualInvitesSent { get; set; }
    public long? ManualInvitesAccepted { get; set; }
    public long? ManualPageVisits { get; set; }
    public long? ManualNewFollowers { get; set; }
    public string? ManualNote { get; set; }
}

/// <summary>Lifetime numbers of one published content, one snapshot per Vietnam day (later reads that day update it).</summary>
public class GrowthPostMetric : BaseEntity
{
    public Guid ContentId { get; set; }
    public GrowthScheduledContent? Content { get; set; }
    public DateOnly Date { get; set; }
    public long? Views { get; set; }
    public long? Reach { get; set; }
    public long? Reactions { get; set; }
    public long? Comments { get; set; }
    public long? Shares { get; set; }
    public long? Clicks { get; set; }
    /// <summary>New followers Meta attributes to this video (Reels "post_video_followers"), when it gives it.</summary>
    public long? Follows { get; set; }
    public DateTime CollectedAtUtc { get; set; }
    public string? Error { get; set; }
}

/// <summary>A competitor Page the owner follows by hand (at most 5 per Page). No scraping, no access to its insights.</summary>
public class GrowthCompetitor : BaseEntity
{
    public Guid FacebookAccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Niche { get; set; }
    public string? Notes { get; set; }
    public ICollection<GrowthCompetitorSnapshot> Snapshots { get; set; } = new List<GrowthCompetitorSnapshot>();
}

/// <summary>What the owner saw on a competitor Page on a day (public numbers typed in, or a notable post).</summary>
public class GrowthCompetitorSnapshot : BaseEntity
{
    public Guid CompetitorId { get; set; }
    public GrowthCompetitor? Competitor { get; set; }
    public DateOnly Date { get; set; }
    public long? Followers { get; set; }
    public int? PostsPerWeek { get; set; }
    public string? PostUrl { get; set; }
    public string? PostTitle { get; set; }
    /// <summary>Reel, Photo, Album, Story, Text, Live.</summary>
    public string? Format { get; set; }
    public string? Topic { get; set; }
    public long? Reactions { get; set; }
    public long? Comments { get; set; }
    public long? Shares { get; set; }
    public long? Views { get; set; }
    public string? Note { get; set; }
}

/// <summary>A generated campaign report (numbers + verdict) kept as it was when made.</summary>
public class GrowthCampaignReport : BaseEntity
{
    public Guid CampaignId { get; set; }
    public GrowthCampaign? Campaign { get; set; }
    public string DataJson { get; set; } = "{}";
    /// <summary>Continue, TestMore, DontBuy, NotEnoughData.</summary>
    public string Verdict { get; set; } = "NotEnoughData";
    public string? AiCommentary { get; set; }
}

/// <summary>Audit trail of the module: who/what changed a campaign or content, publishing attempts and their errors.</summary>
public class GrowthActivityLog : BaseEntity
{
    public Guid? CampaignId { get; set; }
    public Guid? ContentId { get; set; }
    public string Level { get; set; } = "Info"; // Info, Warning, Error
    public string Action { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Actor { get; set; } = "user"; // user, scheduler, system
}

/// <summary>
/// "Chế độ tự động" of a Page: every day the app makes and posts the content by itself — one photo post from an unused
/// drama script (AI picture + AI text), the day's finished video as a Reel, and Stories cut from the Page's videos.
/// The owner only watches; problems (no scripts left, no finished video, Story refused…) are shown as notices.
/// </summary>
public class GrowthAutopilot : BaseEntity
{
    public Guid FacebookAccountId { get; set; }
    public bool Enabled { get; set; }
    public bool ScriptPost { get; set; } = true;
    public bool DailyVideo { get; set; } = true;
    public int StoriesPerDay { get; set; } = 2;
    public DateTime? LastRunAtUtc { get; set; }
    /// <summary>Current notices: [{level, code, message, atUtc}] (JSON).</summary>
    public string NoticesJson { get; set; } = "[]";
    /// <summary>Today's attempts per slot and slots skipped for the day (JSON).</summary>
    public string StateJson { get; set; } = "{}";
}

/// <summary>A drama script available for daily posts (imported from the "Lên kịch bản" library or the owner's own scripts).</summary>
public class GrowthScript : BaseEntity
{
    /// <summary>The script's own code / id in the library (unique).</summary>
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? CategoryName { get; set; }
    public string? Mood { get; set; }
    public string? Era { get; set; }
    public string? Region { get; set; }
    public string Synopsis { get; set; } = string.Empty;
    public string Hook { get; set; } = string.Empty;
    public string EndingNote { get; set; } = string.Empty;
    /// <summary>[{title, visualVi, aiPromptEn, voiceover}] (JSON).</summary>
    public string ScenesJson { get; set; } = "[]";
    /// <summary>library, custom, ai.</summary>
    public string Source { get; set; } = "library";
}

/// <summary>A script used on a Page (one row per Page and script — the unique key is what stops a script being posted twice).</summary>
public class GrowthScriptUsage : BaseEntity
{
    public Guid FacebookAccountId { get; set; }
    public Guid ScriptId { get; set; }
    public GrowthScript? Script { get; set; }
    public Guid? ContentId { get; set; }
}
