using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

/// <summary>
/// One anonymous event on a public landing page (e.g. /cheo-uy-tinh): a visit, a click on
/// "open the Facebook Page" or on one post. A click is NOT a follow — Facebook never tells us that.
/// </summary>
public class LandingEvent : BaseEntity
{
    /// <summary>Landing page slug, e.g. "cheo-uy-tinh".</summary>
    public string Slug { get; set; } = string.Empty;
    /// <summary>view | open_page | open_post</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>Post id for open_post.</summary>
    public string? Target { get; set; }
    /// <summary>mobile | desktop</summary>
    public string Device { get; set; } = "desktop";
    /// <summary>Random id kept in the visitor's browser (no personal data) — counts unique visitors.</summary>
    public string? VisitorId { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    /// <summary>Host of the referring site only (e.g. m.facebook.com).</summary>
    public string? ReferrerHost { get; set; }
    /// <summary>For an open_page click: when the owner marked it "đã trả lại" (interacted back). Null = waiting.</summary>
    public DateTime? HandledAtUtc { get; set; }
}
