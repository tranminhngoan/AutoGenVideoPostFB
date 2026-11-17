namespace NewsStudio.Infrastructure.Common;

/// <summary>Values of <c>ArticleImage.Status</c>.</summary>
/// <summary>What a picture is (ArticleImage.ImageType): shown as a credit on the video and in the review page.</summary>
public static class ImageKinds
{
    /// <summary>Photo published with the news article (shown with the outlet's name).</summary>
    public const string Real = "REAL";
    /// <summary>Freely licensed picture (Wikimedia Commons, Openverse CC) with author and licence.</summary>
    public const string Licensed = "LICENSED";
    public const string AiIllustration = "AI_ILLUSTRATION";
    /// <summary>Chart/graphic drawn by the app from the article's own numbers.</summary>
    public const string Infographic = "INFOGRAPHIC";
}

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
