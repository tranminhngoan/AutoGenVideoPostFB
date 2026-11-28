using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

// ───────────── "Chiến dịch kéo tương tác" (outreach) ─────────────
// Manual-first: the tool keeps lists, drafts texts and tracks clicks; a person posts / joins / comments
// on Facebook by hand. Nothing here posts into Groups (Meta offers no API for that).

/// <summary>Single row: the campaign menu's own password (PBKDF2 hash) and session length.</summary>
public class OutreachSettings : BaseEntity
{
    /// <summary>"pbkdf2-sha256$iterations$saltBase64$hashBase64". Null = use the hash from configuration.</summary>
    public string? PasswordHash { get; set; }
    public int SessionHours { get; set; } = 8;
    /// <summary>Bumped on password change / "lock everywhere": older sessions stop working.</summary>
    public int SessionVersion { get; set; } = 1;
}

/// <summary>One try at the campaign password (kept for the security log and lock-out).</summary>
public class OutreachUnlockAttempt : BaseEntity
{
    public string Ip { get; set; } = string.Empty;
    public bool Success { get; set; }
}

/// <summary>A Facebook Group found by hand (URL pasted / imported from CSV).</summary>
public class OutreachGroup : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Normalised https://www.facebook.com/groups/… link (unique).</summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>Short key used in UTM links, e.g. "g07".</summary>
    public string Key { get; set; } = string.Empty;
    public string? Topic { get; set; }
    /// <summary>Comma list: follow, like, comment, share, view, mixed.</summary>
    public string ExchangeTypes { get; set; } = string.Empty;
    /// <summary>unknown | fit | unfit</summary>
    public string Fit { get; set; } = "unknown";
    /// <summary>public | private | unknown</summary>
    public string Privacy { get; set; } = "unknown";
    public int? Members { get; set; }
    /// <summary>The group's own rules (what is allowed to post).</summary>
    public string? Rules { get; set; }
    public string? Note { get; set; }
}

/// <summary>A saved invitation text (written by AI or by hand).</summary>
public class OutreachContent : BaseEntity
{
    public string Text { get; set; } = string.Empty;
    /// <summary>friendly | short | funny | custom</summary>
    public string Style { get; set; } = "friendly";
    public string? Topic { get; set; }
    /// <summary>ai | manual</summary>
    public string Origin { get; set; } = "manual";
    public int CopiedCount { get; set; }
}

public class OutreachCampaign : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    /// <summary>utm_campaign value, e.g. "campaign_01" (unique).</summary>
    public string Slug { get; set; } = string.Empty;
    /// <summary>JSON array of FacebookAccount ids (the helper Pages used).</summary>
    public string PageIdsJson { get; set; } = "[]";
    /// <summary>JSON array of OutreachGroup ids.</summary>
    public string GroupIdsJson { get; set; } = "[]";
    public string TargetPath { get; set; } = "/cheo-uy-tinh";
    public Guid? ContentId { get; set; }
    public string? ContentText { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    /// <summary>Draft | PendingApproval | Running | Paused | Completed</summary>
    public string Status { get; set; } = "Draft";
    public string? Owner { get; set; }
    public string? ResultNote { get; set; }
}

/// <summary>"Done by hand" record for one Group × Page in a campaign (posted / joined / commented).</summary>
public class OutreachActivity : BaseEntity
{
    public Guid CampaignId { get; set; }
    public Guid GroupId { get; set; }
    public Guid? FacebookAccountId { get; set; }
    /// <summary>post | join | comment</summary>
    public string Kind { get; set; } = "post";
    public string? PostUrl { get; set; }
    public string? Note { get; set; }
    public DateTime DoneAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A Group post the owner wants to answer by hand, with an AI-suggested comment.</summary>
public class OutreachPostTask : BaseEntity
{
    public Guid? GroupId { get; set; }
    public Guid? CampaignId { get; set; }
    public string PostUrl { get; set; } = string.Empty;
    /// <summary>The post's text, pasted by the owner (Group posts can't be read through the API).</summary>
    public string? PostText { get; set; }
    public string? SuggestedComment { get; set; }
    /// <summary>todo | done | skipped</summary>
    public string Status { get; set; } = "todo";
    public DateTime? DoneAtUtc { get; set; }
    public string? Note { get; set; }
}
