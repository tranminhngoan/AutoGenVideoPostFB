namespace NewsStudio.Application.DTOs;

// ───────────────────────── Pages & capabilities ─────────────────────────

/// <summary>A Page from the existing connection (never its token).</summary>
public class GrowthPageDto
{
    public Guid Id { get; set; }
    public string PageId { get; set; } = string.Empty;
    public string PageName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool HasToken { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime? TokenExpiresAtUtc { get; set; }
    public string? InstagramUsername { get; set; }
    /// <summary>Public Page picture (Graph API picture edge — needs no token).</summary>
    public string PictureUrl { get; set; } = string.Empty;
    public long? LatestFollowers { get; set; }
    public DateOnly? LatestFollowersDate { get; set; }
    public Guid? ActiveCampaignId { get; set; }
    public string? ActiveCampaignName { get; set; }
}

/// <summary>One row of "what this Page's token can do", checked live against Meta.</summary>
public class GrowthCapabilityDto
{
    public string Key { get; set; } = string.Empty;
    public string Feature { get; set; } = string.Empty;
    public string Api { get; set; } = string.Empty;
    public string Permissions { get; set; } = string.Empty;
    /// <summary>Yes, No, Unknown (cannot be checked without posting), Manual (no API exists).</summary>
    public string Available { get; set; } = "Unknown";
    public string? Detail { get; set; }
}

public class GrowthCapabilitiesDto
{
    public Guid FacebookAccountId { get; set; }
    public string GraphApiVersion { get; set; } = string.Empty;
    public List<string> GrantedPermissions { get; set; } = new();
    public string? TokenError { get; set; }
    public DateTime CheckedAtUtc { get; set; }
    public List<GrowthCapabilityDto> Items { get; set; } = new();
}

// ───────────────────────── Campaigns ─────────────────────────

public class GrowthPlanDayDto
{
    public int Day { get; set; }
    public DateOnly? Date { get; set; }
    public string Focus { get; set; } = string.Empty;
    public List<string> Tasks { get; set; } = new();
}

public class GrowthChecklistItemDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Help { get; set; } = string.Empty;
    public bool Done { get; set; }
    public string? Note { get; set; }
    public DateTime? AtUtc { get; set; }
}

public class GrowthMetricAvailabilityDto
{
    public string Metric { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public DateTime? AtUtc { get; set; }
}

public class GrowthCampaignDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? FacebookAccountId { get; set; }
    public string PageName { get; set; } = string.Empty;
    public string? PageId { get; set; }
    public bool PageConnected { get; set; }
    public string Status { get; set; } = "Draft";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int TotalDays { get; set; }
    /// <summary>1-based day of the campaign today (0 = not started yet, TotalDays+1 = over).</summary>
    public int DayNumber { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public long? TargetFollowers { get; set; }
    public long? TargetViews { get; set; }
    public int ReelsPerDay { get; set; }
    public int PhotosPerDay { get; set; }
    public int StoriesPerDay { get; set; }
    public List<string> TimeSlots { get; set; } = new();
    public decimal AiBudgetUsd { get; set; }
    public decimal AiSpentUsd { get; set; }
    public string ApprovalMode { get; set; } = "Review";
    public long? StartingFollowers { get; set; }
    public DateTime? StartingFollowersAtUtc { get; set; }
    public long? EndingFollowers { get; set; }
    public DateTime? EndingFollowersAtUtc { get; set; }
    public decimal? MetaOneCost { get; set; }
    public string MetaOneCurrency { get; set; } = "VND";
    public List<GrowthChecklistItemDto> Checklist { get; set; } = new();
    public List<GrowthPlanDayDto> Plan { get; set; } = new();
    public List<GrowthMetricAvailabilityDto> MetricAvailability { get; set; } = new();
    public DateTime? LastSyncedAtUtc { get; set; }
    public string? LastSyncError { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Dictionary<string, int> ContentCounts { get; set; } = new();
}

public class GrowthCampaignInputDto
{
    public string? Name { get; set; }
    public Guid? FacebookAccountId { get; set; }
    public DateOnly? StartDate { get; set; }
    /// <summary>Length in days (1–31); EndDate wins when both are given.</summary>
    public int? Days { get; set; }
    public DateOnly? EndDate { get; set; }
    public long? TargetFollowers { get; set; }
    public long? TargetViews { get; set; }
    public int? ReelsPerDay { get; set; }
    public int? PhotosPerDay { get; set; }
    public int? StoriesPerDay { get; set; }
    public List<string>? TimeSlots { get; set; }
    public decimal? AiBudgetUsd { get; set; }
    public string? ApprovalMode { get; set; }
    public decimal? MetaOneCost { get; set; }
    public string? MetaOneCurrency { get; set; }
    public string? Notes { get; set; }
    public List<GrowthPlanDayDto>? Plan { get; set; }
}

public class GrowthChecklistUpdateDto
{
    public string Key { get; set; } = string.Empty;
    public bool Done { get; set; }
    public string? Note { get; set; }
}

/// <summary>Numbers the owner reads in the Facebook app (Meta One invitations…) and types in for one day.</summary>
public class GrowthManualMetricsDto
{
    public DateOnly Date { get; set; }
    public long? InvitesSent { get; set; }
    public long? InvitesAccepted { get; set; }
    public long? PageVisits { get; set; }
    public long? NewFollowers { get; set; }
    public string? Note { get; set; }
}

// ───────────────────────── Content & assets ─────────────────────────

public class GrowthAssetDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "Image";
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double? DurationSeconds { get; set; }
    public string Source { get; set; } = "Upload";
    public string Url { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public class GrowthPostMetricDto
{
    public DateOnly Date { get; set; }
    public long? Views { get; set; }
    public long? Reach { get; set; }
    public long? Reactions { get; set; }
    public long? Comments { get; set; }
    public long? Shares { get; set; }
    public long? Clicks { get; set; }
    public long? Follows { get; set; }
    public long? Engagement { get; set; }
    public DateTime CollectedAtUtc { get; set; }
    public string? Error { get; set; }
}

public class GrowthContentDto
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public string Kind { get; set; } = "Photo";
    public string Status { get; set; } = "Draft";
    public string Title { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string Hashtags { get; set; } = string.Empty;
    public string? Style { get; set; }
    public string? Cta { get; set; }
    public string? Topic { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public string? SourceTitle { get; set; }
    public string? SourcePreviewUrl { get; set; }
    public List<GrowthAssetDto> Assets { get; set; } = new();
    public DateTime? ScheduledAtUtc { get; set; }
    public bool ManualPublish { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }
    public string? ErrorKind { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public string? ExternalPostId { get; set; }
    public string? PermalinkUrl { get; set; }
    public bool PublishedManually { get; set; }
    public GrowthPostMetricDto? LatestMetric { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public class GrowthContentInputDto
{
    public string? Kind { get; set; }
    public string? Title { get; set; }
    public string? Caption { get; set; }
    public string? Hashtags { get; set; }
    public string? Style { get; set; }
    public string? Cta { get; set; }
    public string? Topic { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public List<Guid>? AssetIds { get; set; }
    public DateTime? ScheduledAtUtc { get; set; }
    public bool? ManualPublish { get; set; }
    /// <summary>Same key twice → the first item is returned, nothing new is made.</summary>
    public string? IdempotencyKey { get; set; }
    /// <summary>false = keep as Draft; true = send to the calendar (PendingApproval in Review mode, Scheduled in Auto mode).</summary>
    public bool? Submit { get; set; }
}

public class GrowthMarkManualDto
{
    public string? PermalinkUrl { get; set; }
}

/// <summary>A finished video from the existing video tools that can be scheduled as a Reel / Story.</summary>
public class GrowthVideoSourceDto
{
    public string Type { get; set; } = string.Empty; // StudioVideo, AiStudioProject, VideoProject
    public string TypeLabel { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public double? DurationSeconds { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid? FacebookAccountId { get; set; }
    public bool AlreadyPublished { get; set; }
    public string? PreviewUrl { get; set; }
    public bool Scheduled { get; set; }
}

// ───────────────────────── AI ─────────────────────────

public class GrowthCaptionRequestDto
{
    /// <summary>Topic / what the post is about (or use a source).</summary>
    public string? Brief { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public List<string>? Styles { get; set; }
    public int? Variants { get; set; }
    public string? Kind { get; set; }
}

public class GrowthCaptionVariantDto
{
    public string Style { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public List<string> Hashtags { get; set; } = new();
    public string? Question { get; set; }
}

public class GrowthCaptionResultDto
{
    public List<GrowthCaptionVariantDto> Variants { get; set; } = new();
    public decimal CostUsd { get; set; }
    public decimal CampaignAiSpentUsd { get; set; }
}

public class GrowthRepurposeRequestDto
{
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
}

public class GrowthPhotoIdeaDto
{
    public string Headline { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
}

public class GrowthStoryIdeaDto
{
    public string Text { get; set; } = string.Empty;
    public string? Sticker { get; set; }
}

public class GrowthRepurposeResultDto
{
    public string SourceTitle { get; set; } = string.Empty;
    public List<GrowthPhotoIdeaDto> PhotoPosts { get; set; } = new();
    public List<GrowthStoryIdeaDto> Stories { get; set; } = new();
    public List<GrowthCaptionVariantDto> Captions { get; set; } = new();
    public List<string> Titles { get; set; } = new();
    public List<string> Hashtags { get; set; } = new();
    public string CommentQuestion { get; set; } = string.Empty;
    public string FollowCta { get; set; } = string.Empty;
    public decimal CostUsd { get; set; }
}

public class GrowthSuggestionsDto
{
    public List<string> Suggestions { get; set; } = new();
    public string Basis { get; set; } = string.Empty;
    public bool FromAi { get; set; }
    public decimal CostUsd { get; set; }
}

// ───────────────────────── Overview & analytics ─────────────────────────

public class GrowthKpiDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public double? Value { get; set; }
    public string Unit { get; set; } = "";
    /// <summary>Why there is no value (missing permission, metric retired by Meta, not enough data…).</summary>
    public string? Note { get; set; }
    /// <summary>meta, manual, app (counted by the app), computed.</summary>
    public string Source { get; set; } = "meta";
}

public class GrowthDayPointDto
{
    public DateOnly Date { get; set; }
    public long? Followers { get; set; }
    public long? NewFollows { get; set; }
    public long? Unfollows { get; set; }
    public long? PageViews { get; set; }
    public long? MediaViews { get; set; }
    public long? MediaViewers { get; set; }
    public long? Engagements { get; set; }
    public long? ContentViews { get; set; }
    public long? ContentEngagement { get; set; }
    public int Published { get; set; }
    public long? ManualInvitesSent { get; set; }
    public long? ManualInvitesAccepted { get; set; }
    public long? ManualPageVisits { get; set; }
    public long? ManualNewFollowers { get; set; }
}

public class GrowthKindStatsDto
{
    public string Kind { get; set; } = string.Empty;
    public int Published { get; set; }
    public int WithMetrics { get; set; }
    public long? Views { get; set; }
    public long? Reach { get; set; }
    public long? Engagement { get; set; }
    public long? Shares { get; set; }
    public long? Comments { get; set; }
    public double? AvgViews { get; set; }
    public double? AvgEngagement { get; set; }
}

public class GrowthPeriodDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public long? FollowersStart { get; set; }
    public long? FollowersEnd { get; set; }
    public long? FollowerGain { get; set; }
    public long? NewFollows { get; set; }
    public long? PageViews { get; set; }
    public long? MediaViews { get; set; }
    public long? Engagements { get; set; }
    public int DaysWithData { get; set; }
}

public class GrowthProgressDto
{
    public int Day { get; set; }
    public int TotalDays { get; set; }
    public double TimePercent { get; set; }
    public int Planned { get; set; }
    public int Published { get; set; }
    public double? FollowerTargetPercent { get; set; }
    public double? ViewTargetPercent { get; set; }
}

public class GrowthOverviewDto
{
    public GrowthCampaignDto Campaign { get; set; } = new();
    public GrowthPageDto? Page { get; set; }
    public long? CurrentFollowers { get; set; }
    public DateTime? CurrentFollowersAtUtc { get; set; }
    public List<GrowthKpiDto> Kpis { get; set; } = new();
    public List<GrowthDayPointDto> Daily { get; set; } = new();
    public List<GrowthKindStatsDto> ByKind { get; set; } = new();
    public GrowthPeriodDto? Before { get; set; }
    public GrowthPeriodDto? During { get; set; }
    public List<GrowthContentDto> Today { get; set; } = new();
    public List<GrowthContentDto> Upcoming { get; set; } = new();
    public List<GrowthContentDto> Failed { get; set; } = new();
    public List<GrowthContentDto> DueManual { get; set; } = new();
    public GrowthProgressDto Progress { get; set; } = new();
    public List<string> Insights { get; set; } = new();
}

public class GrowthPostAnalyticsDto
{
    public Guid ContentId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CaptionExcerpt { get; set; } = string.Empty;
    public int CaptionLength { get; set; }
    public int HashtagCount { get; set; }
    public string? Style { get; set; }
    public string? Cta { get; set; }
    public string? Topic { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public string? LocalDate { get; set; }
    public string? LocalTime { get; set; }
    public long? Views { get; set; }
    public long? Reach { get; set; }
    public long? Reactions { get; set; }
    public long? Comments { get; set; }
    public long? Shares { get; set; }
    public long? Engagement { get; set; }
    public long? Follows { get; set; }
    public string? PermalinkUrl { get; set; }
    public bool PublishedManually { get; set; }
    public string? MetricError { get; set; }
}

public class GrowthRankingDto
{
    public List<GrowthPostAnalyticsDto> TopViews { get; set; } = new();
    public List<GrowthPostAnalyticsDto> TopReach { get; set; } = new();
    public List<GrowthPostAnalyticsDto> TopShares { get; set; } = new();
    public List<GrowthPostAnalyticsDto> TopEngagement { get; set; } = new();
    public List<GrowthPostAnalyticsDto> TopFollows { get; set; } = new();
    /// <summary>Seen a lot but brought (attributed) no follower — only when Meta gives per-post follows.</summary>
    public List<GrowthPostAnalyticsDto> HighViewsNoFollows { get; set; } = new();
    public List<GrowthPostAnalyticsDto> Weakest { get; set; } = new();
    public bool FollowAttributionAvailable { get; set; }
}

public class GrowthHourBucketDto
{
    public int Hour { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Posts { get; set; }
    public double? AvgViews { get; set; }
    public double? AvgEngagement { get; set; }
}

public class GrowthHoursDto
{
    public List<GrowthHourBucketDto> Buckets { get; set; } = new();
    public int Samples { get; set; }
    public int MinSamples { get; set; }
    public bool EnoughData { get; set; }
    public List<string> SuggestedSlots { get; set; } = new();
    public string Basis { get; set; } = string.Empty;
}

public class GrowthFollowerDayDto
{
    public DateOnly Date { get; set; }
    public long? Followers { get; set; }
    public long? NewFollows { get; set; }
    public long? Unfollows { get; set; }
    public long? NetChange { get; set; }
    public long? PageVisits { get; set; }
    public string? PageVisitsSource { get; set; }
    public double? ConversionPercent { get; set; }
    public int Published { get; set; }
    public long? InvitesSent { get; set; }
    public long? InvitesAccepted { get; set; }
}

public class GrowthFollowersDto
{
    public long? Starting { get; set; }
    public long? Current { get; set; }
    public long? Gain { get; set; }
    public double? GrowthPercent { get; set; }
    public long? PageVisits { get; set; }
    public double? ConversionPercent { get; set; }
    public string? ConversionNote { get; set; }
    public List<GrowthFollowerDayDto> Days { get; set; } = new();
    public List<GrowthKindStatsDto> ByKind { get; set; } = new();
    public string? BestKind { get; set; }
    public long? InvitesSent { get; set; }
    public long? InvitesAccepted { get; set; }
}

// ───────────────────────── Competitors ─────────────────────────

public class GrowthCompetitorSnapshotDto
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public long? Followers { get; set; }
    public int? PostsPerWeek { get; set; }
    public string? PostUrl { get; set; }
    public string? PostTitle { get; set; }
    public string? Format { get; set; }
    public string? Topic { get; set; }
    public long? Reactions { get; set; }
    public long? Comments { get; set; }
    public long? Shares { get; set; }
    public long? Views { get; set; }
    public string? Note { get; set; }
}

public class GrowthCompetitorDto
{
    public Guid Id { get; set; }
    public Guid FacebookAccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Niche { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<GrowthCompetitorSnapshotDto> Snapshots { get; set; } = new();
    public long? LatestFollowers { get; set; }
    public int? LatestPostsPerWeek { get; set; }
}

public class GrowthCompetitorInputDto
{
    public Guid? FacebookAccountId { get; set; }
    public string? Name { get; set; }
    public string? Url { get; set; }
    public string? Niche { get; set; }
    public string? Notes { get; set; }
}

public class GrowthCompetitorSnapshotInputDto
{
    public DateOnly? Date { get; set; }
    public long? Followers { get; set; }
    public int? PostsPerWeek { get; set; }
    public string? PostUrl { get; set; }
    public string? PostTitle { get; set; }
    public string? Format { get; set; }
    public string? Topic { get; set; }
    public long? Reactions { get; set; }
    public long? Comments { get; set; }
    public long? Shares { get; set; }
    public long? Views { get; set; }
    public string? Note { get; set; }
}

public class GrowthCompetitorAnalysisDto
{
    public List<GrowthCountDto> Formats { get; set; } = new();
    public List<GrowthCountDto> Topics { get; set; } = new();
    public List<GrowthCompetitorSnapshotDto> TopPosts { get; set; } = new();
    public List<GrowthCountDto> PostingFrequency { get; set; } = new();
    public int Observations { get; set; }
    public List<string> AiNotes { get; set; } = new();
    public decimal CostUsd { get; set; }
}

public class GrowthCountDto
{
    public string Label { get; set; } = string.Empty;
    public double Value { get; set; }
    public double? Extra { get; set; }
}

// ───────────────────────── Report ─────────────────────────

public class GrowthReportDataDto
{
    public string CampaignName { get; set; } = string.Empty;
    public string PageName { get; set; } = string.Empty;
    public string? PageId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? FollowersStart { get; set; }
    public long? FollowersEnd { get; set; }
    public long? FollowerGain { get; set; }
    public double? GrowthPercent { get; set; }
    public long? TotalViews { get; set; }
    public long? TotalReach { get; set; }
    public long? TotalEngagement { get; set; }
    public int TotalPublished { get; set; }
    public List<GrowthKindStatsDto> ByKind { get; set; } = new();
    public long? MetaOneInvitesSent { get; set; }
    public long? MetaOneInvitesAccepted { get; set; }
    public List<GrowthChecklistItemDto> MetaOneChecklist { get; set; } = new();
    public List<GrowthPostAnalyticsDto> Top { get; set; } = new();
    public List<GrowthPostAnalyticsDto> Weak { get; set; } = new();
    public List<string> BestSlots { get; set; } = new();
    public string BestSlotsBasis { get; set; } = string.Empty;
    public decimal AiCostUsd { get; set; }
    public decimal VideoCostUsd { get; set; }
    public decimal ContentCostUsd { get; set; }
    public decimal? MetaOneCost { get; set; }
    public string MetaOneCurrency { get; set; } = "VND";
    public decimal? ContentCostPerFollowerUsd { get; set; }
    public decimal? MetaOneCostPerFollower { get; set; }
    public GrowthPeriodDto? Before { get; set; }
    public GrowthPeriodDto? During { get; set; }
    public List<string> Recommendations { get; set; } = new();
    public List<string> DataGaps { get; set; } = new();
}

public class GrowthReportDto
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Verdict { get; set; } = "NotEnoughData";
    public string VerdictLabel { get; set; } = string.Empty;
    public string VerdictReason { get; set; } = string.Empty;
    public string? AiCommentary { get; set; }
    public GrowthReportDataDto Data { get; set; } = new();
}

public class GrowthActivityLogDto
{
    public Guid Id { get; set; }
    public Guid? ContentId { get; set; }
    public string Level { get; set; } = "Info";
    public string Action { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Actor { get; set; } = "user";
    public DateTime CreatedAtUtc { get; set; }
}

public class GrowthSyncResultDto
{
    public bool FollowersRead { get; set; }
    public int MetricsAvailable { get; set; }
    public int MetricsUnavailable { get; set; }
    public int PostsRead { get; set; }
    public int PostErrors { get; set; }
    public List<string> Errors { get; set; } = new();
}
