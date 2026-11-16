namespace NewsStudio.Infrastructure.Common;

/// <summary>Values of <c>ArticleImage.Status</c>.</summary>
public static class ImageStatus
{
    public const string Extracted = "Extracted";
    public const string Excluded = "Excluded";
    /// <summary>Could not be downloaded (blocked, 401/403, not a picture, too small).</summary>
    public const string Unavailable = "Unavailable";
    /// <summary>Related Creative Commons illustration found on the web (article photos were unavailable).</summary>
    public const string WebSearch = "WebSearch";
    /// <summary>A web illustration the editor switched off.</summary>
    public const string WebSearchExcluded = "WebSearchExcluded";

    public static bool IsWebSearch(string? status) => status is WebSearch or WebSearchExcluded;

    public static bool IsSelectedForVideo(string? status) => status is not (Excluded or WebSearchExcluded or Unavailable);

    /// <summary>Status after the editor ticks/unticks an image, keeping the "web illustration" marker.</summary>
    public static string Toggle(string? current, bool selected) => IsWebSearch(current)
        ? (selected ? WebSearch : WebSearchExcluded)
        : (selected ? Extracted : Excluded);
}
