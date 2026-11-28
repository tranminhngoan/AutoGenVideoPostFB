namespace NewsStudio.Application.DTOs;

// ── Security ──
public class OutreachUnlockRequestDto { public string Password { get; set; } = string.Empty; }

public class OutreachUnlockResultDto
{
    public bool Ok { get; set; }
    public string? Token { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string? Error { get; set; }
    /// <summary>Tries left before the lock-out; null when locked.</summary>
    public int? AttemptsLeft { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
}

public class OutreachSecurityStatusDto
{
    public bool PasswordConfigured { get; set; }
    /// <summary>"database" (changed in the menu) or "config" (Railway variable / appsettings).</summary>
    public string PasswordSource { get; set; } = "config";
    public int SessionHours { get; set; }
    public int MaxAttempts { get; set; }
    public int LockMinutes { get; set; }
    public List<OutreachAttemptDto> RecentAttempts { get; set; } = new();
}

public class OutreachAttemptDto
{
    public DateTime AtUtc { get; set; }
    public string Ip { get; set; } = string.Empty;
    public bool Success { get; set; }
}

public class OutreachChangePasswordDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public int? SessionHours { get; set; }
}

// ── Pages ──
public class OutreachPageDto
{
    public Guid Id { get; set; }
    public string PageId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PictureUrl { get; set; } = string.Empty;
    /// <summary>The Page promoted by /cheo-uy-tinh (not a helper).</summary>
    public bool IsMain { get; set; }
    /// <summary>One of the helper Pages allowed in campaigns (Outreach:HelperPageIds).</summary>
    public bool IsHelper { get; set; }
    public string Status { get; set; } = "Active";
    /// <summary>ok | warning | error — from Meta's own check of the stored token.</summary>
    public string Health { get; set; } = "ok";
    public string? Problem { get; set; }
    public List<string> Permissions { get; set; } = new();
    public DateTime? TokenExpiresAtUtc { get; set; }
}

// ── Groups ──
public class OutreachGroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string? Topic { get; set; }
    public List<string> ExchangeTypes { get; set; } = new();
    public string Fit { get; set; } = "unknown";
    public string Privacy { get; set; } = "unknown";
    public int? Members { get; set; }
    public string? Rules { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Which helper Pages are in this Group (set by hand).</summary>
    public List<OutreachMembershipDto> Memberships { get; set; } = new();
    /// <summary>Visits / clicks that came from links posted in this group (utm_content).</summary>
    public int Views { get; set; }
    public int Clicks { get; set; }
}

public class OutreachGroupInputDto
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Topic { get; set; }
    public List<string>? ExchangeTypes { get; set; }
    public string? Fit { get; set; }
    public string? Privacy { get; set; }
    public int? Members { get; set; }
    public string? Rules { get; set; }
    public string? Note { get; set; }
}

public class OutreachImportResultDto
{
    public int Added { get; set; }
    public int Duplicates { get; set; }
    public List<string> Invalid { get; set; } = new();
}

public class OutreachGroupSearchDto
{
    public string Keyword { get; set; } = string.Empty;
    /// <summary>Facebook's own group search for the keyword — opened by the owner in their browser.</summary>
    public string FacebookSearchUrl { get; set; } = string.Empty;
    public List<string> RelatedKeywords { get; set; } = new();
}

// ── AI outreach / content library ──
public class OutreachWriteRequestDto
{
    /// <summary>friendly | short | funny</summary>
    public string Style { get; set; } = "friendly";
    public string? Topic { get; set; }
    public Guid? GroupId { get; set; }
    public int Count { get; set; } = 3;
    public string? Extra { get; set; }
}

public class OutreachDraftDto
{
    public string Text { get; set; } = string.Empty;
    /// <summary>0–100: how close it is to a text already in the library (≥ 70 = near duplicate).</summary>
    public int Similarity { get; set; }
    public string? SimilarTo { get; set; }
}

public class OutreachContentDto
{
    public Guid Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Style { get; set; } = "friendly";
    public string? Topic { get; set; }
    public string Origin { get; set; } = "manual";
    public int CopiedCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class OutreachContentInputDto
{
    public string Text { get; set; } = string.Empty;
    public string? Style { get; set; }
    public string? Topic { get; set; }
    public string? Origin { get; set; }
}

public class OutreachTextCheckDto { public string Text { get; set; } = string.Empty; }

// ── Campaigns ──
public class OutreachCampaignDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public List<Guid> PageIds { get; set; } = new();
    public List<Guid> GroupIds { get; set; } = new();
    public string TargetPath { get; set; } = "/cheo-uy-tinh";
    public Guid? ContentId { get; set; }
    public string? ContentText { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Status { get; set; } = "Draft";
    public string? Owner { get; set; }
    public string? ResultNote { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int Views { get; set; }
    public int Clicks { get; set; }
    public int ActivitiesDone { get; set; }
    /// <summary>Group × Page pairs to work through.</summary>
    public int ActivitiesPlanned { get; set; }
}

public class OutreachCampaignInputDto
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public List<Guid>? PageIds { get; set; }
    public List<Guid>? GroupIds { get; set; }
    public Guid? ContentId { get; set; }
    public string? ContentText { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Status { get; set; }
    public string? Owner { get; set; }
    public string? ResultNote { get; set; }
}

/// <summary>One Group × helper Page of a campaign: its tracking link and whether it was done by hand.</summary>
public class OutreachCampaignLinkDto
{
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string GroupUrl { get; set; } = string.Empty;
    public Guid? PageId { get; set; }
    public string? PageName { get; set; }
    public string UtmContent { get; set; } = string.Empty;
    /// <summary>Whether this helper Page is in the Group: null (not asked) | requested | joined | rejected.</summary>
    public string? Membership { get; set; }
    /// <summary>Path + query, e.g. /cheo-uy-tinh?utm_source=facebook&amp;… (the frontend adds its own domain).</summary>
    public string Link { get; set; } = string.Empty;
    public int Views { get; set; }
    public int Clicks { get; set; }
    public List<OutreachActivityDto> Activities { get; set; } = new();
}

public class OutreachActivityDto
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid? PageId { get; set; }
    public string Kind { get; set; } = "post";
    public string? PostUrl { get; set; }
    public string? Note { get; set; }
    public DateTime DoneAtUtc { get; set; }
}

public class OutreachActivityInputDto
{
    public Guid GroupId { get; set; }
    public Guid? PageId { get; set; }
    public string Kind { get; set; } = "post";
    public string? PostUrl { get; set; }
    public string? Note { get; set; }
}

// ── Group posts to answer by hand ──
public class OutreachPostTaskDto
{
    public Guid Id { get; set; }
    public Guid? GroupId { get; set; }
    public string? GroupName { get; set; }
    public Guid? CampaignId { get; set; }
    public string PostUrl { get; set; } = string.Empty;
    public string? PostText { get; set; }
    public string? SuggestedComment { get; set; }
    public string Status { get; set; } = "todo";
    public DateTime? DoneAtUtc { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class OutreachPostTaskInputDto
{
    public Guid? GroupId { get; set; }
    public Guid? CampaignId { get; set; }
    public string PostUrl { get; set; } = string.Empty;
    public string? PostText { get; set; }
    public string? SuggestedComment { get; set; }
    public string? Status { get; set; }
    public string? Note { get; set; }
}

// ── Stats ──
public class OutreachStatsDto
{
    public int Days { get; set; }
    public int Views { get; set; }
    public int Clicks { get; set; }
    public int UniqueVisitors { get; set; }
    public int MobileViews { get; set; }
    public int DesktopViews { get; set; }
    public List<OutreachDayDto> PerDay { get; set; } = new();
    public List<OutreachBreakdownDto> Sources { get; set; } = new();
    public List<OutreachBreakdownDto> Campaigns { get; set; } = new();
    public List<OutreachBreakdownDto> Contents { get; set; } = new();
}

public class OutreachDayDto
{
    /// <summary>yyyy-MM-dd, Vietnam time.</summary>
    public string Date { get; set; } = string.Empty;
    public int Views { get; set; }
    public int Clicks { get; set; }
}

public class OutreachBreakdownDto
{
    public string Key { get; set; } = string.Empty;
    /// <summary>Readable name (campaign name / "Showbiz × Group abc") when known.</summary>
    public string? Label { get; set; }
    public int Views { get; set; }
    public int Clicks { get; set; }
}

public class OutreachOverviewDto
{
    public int Groups { get; set; }
    public int GroupsFit { get; set; }
    public int Contents { get; set; }
    public int CampaignsRunning { get; set; }
    public int Campaigns { get; set; }
    public int PostTasksTodo { get; set; }
    public int Views7d { get; set; }
    public int Clicks7d { get; set; }
    public int OutreachViews7d { get; set; }
    public int OutreachClicks7d { get; set; }
    public List<OutreachPageDto> Pages { get; set; } = new();
}

// ── Group discovery (Brave Search) + per-Page membership ──
public class OutreachMembershipDto
{
    public Guid PageId { get; set; }
    /// <summary>requested | joined | rejected</summary>
    public string Status { get; set; } = "requested";
    public DateTime UpdatedAtUtc { get; set; }
}

public class OutreachMembershipInputDto
{
    /// <summary>requested | joined | rejected | none (forget)</summary>
    public string Status { get; set; } = "requested";
}

public class OutreachDiscoveredGroupDto
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? Members { get; set; }
    /// <summary>public | private | unknown (from the search snippet)</summary>
    public string Privacy { get; set; } = "unknown";
    /// <summary>Already in the Group list (its id).</summary>
    public Guid? GroupId { get; set; }
}

public class OutreachDiscoverResultDto
{
    public string Keyword { get; set; } = string.Empty;
    public int Page { get; set; }
    public bool HasMore { get; set; }
    public string FacebookSearchUrl { get; set; } = string.Empty;
    public List<OutreachDiscoveredGroupDto> Results { get; set; } = new();
}
