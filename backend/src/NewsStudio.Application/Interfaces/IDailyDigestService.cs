using NewsStudio.Application.DTOs;

namespace NewsStudio.Application.Interfaces;

/// <summary>
/// Daily "Điểm tin nóng" of every Facebook Page: once a day (evening), the most striking stories of the
/// page's topic, each as a picture with a short summary on it, posted as one wall post and one Story.
/// </summary>
public interface IDailyDigestService
{
    /// <summary>Recurring job: makes and posts the digest of every page whose digest time has come.</summary>
    Task TickAsync(CancellationToken cancellationToken = default);

    /// <summary>Makes the pictures of today's digest for a page without posting anything.</summary>
    /// <param name="kind">"Daily" / "Weekly"; null = today's kind (weekly on Sundays when enabled).</param>
    Task<DailyDigestRunDto> PreviewAsync(Guid facebookAccountId, string? kind = null, CancellationToken cancellationToken = default);

    /// <summary>Makes and posts today's digest for a page right now.</summary>
    Task<DailyDigestRunDto> PublishNowAsync(Guid facebookAccountId, CancellationToken cancellationToken = default);

    /// <summary>Digests of the last 7 days (all pages, or one page), newest first.</summary>
    Task<List<DailyDigestRunDto>> GetRecentAsync(Guid? facebookAccountId, CancellationToken cancellationToken = default);

    /// <summary>Local path of one picture of a digest ("post_01.jpg", "story_00.jpg"), or null.</summary>
    Task<string?> GetImagePathAsync(Guid runId, string fileName, CancellationToken cancellationToken = default);
}
