namespace NewsStudio.Application.DTOs;

/// <summary>Public, display-only info about a connected Facebook Page (never tokens or internal ids).</summary>
public class PageShowcaseDto
{
    public string PageId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? About { get; set; }
    public string PictureUrl { get; set; } = string.Empty;
    public string? CoverUrl { get; set; }
    public string? Link { get; set; }
    public long? Followers { get; set; }
    public List<ShowcasePostDto> Posts { get; set; } = new();
}

public class ShowcasePostDto
{
    public string Id { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsVideo { get; set; }
    public string PermalinkUrl { get; set; } = string.Empty;
    public DateTime? CreatedTime { get; set; }
}

public class LandingEventRequestDto
{
    public string Slug { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Target { get; set; }
    public string? Device { get; set; }
    public string? VisitorId { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? Referrer { get; set; }
}

public class LandingStatsDto
{
    public string Slug { get; set; } = string.Empty;
    public int Days { get; set; }
    public int Views { get; set; }
    public int UniqueVisitors { get; set; }
    public int OpenPageClicks { get; set; }
    public int PostClicks { get; set; }
    public int MobileViews { get; set; }
    public int DesktopViews { get; set; }
    public List<LandingCountDto> Sources { get; set; } = new();
    public List<LandingCountDto> TopPosts { get; set; } = new();
}

public class LandingCountDto
{
    public string Key { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>One click on "Mở Facebook – ủng hộ Page" (the "Chéo lại" list).</summary>
public class LandingClickDto
{
    public Guid Id { get; set; }
    public DateTime ClickedAtUtc { get; set; }
    public string Device { get; set; } = "desktop";
    /// <summary>utm_source, else the referring site, else null (opened directly).</summary>
    public string? Source { get; set; }
    /// <summary>Short anonymous id of the browser (same person = same id), for spotting repeat clicks.</summary>
    public string? Visitor { get; set; }
    /// <summary>How many times this browser clicked the button in total.</summary>
    public int VisitorClicks { get; set; }
    public DateTime? HandledAtUtc { get; set; }
}

public class LandingClickHandledDto
{
    public bool Handled { get; set; }
}
