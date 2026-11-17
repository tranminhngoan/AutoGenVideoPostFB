using Microsoft.EntityFrameworkCore;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Article> Articles { get; }
    DbSet<ArticleImage> ArticleImages { get; }
    DbSet<VideoProject> VideoProjects { get; }
    DbSet<VideoScene> VideoScenes { get; }
    DbSet<VideoScript> VideoScripts { get; }
    DbSet<VideoAsset> VideoAssets { get; }
    DbSet<ProcessingJob> ProcessingJobs { get; }
    DbSet<VideoReview> VideoReviews { get; }
    DbSet<FacebookAccount> FacebookAccounts { get; }
    DbSet<FacebookPost> FacebookPosts { get; }
    DbSet<VideoMetric> VideoMetrics { get; }
    DbSet<SystemSetting> SystemSettings { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<TrendingArticle> TrendingArticles { get; }
    DbSet<AutoPostSettings> AutoPostSettings { get; }
    DbSet<AutoPostRun> AutoPostRuns { get; }
    DbSet<DailyDigestRun> DailyDigestRuns { get; }
    DbSet<InstagramMetric> InstagramMetrics { get; }
    DbSet<AiUsage> AiUsages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
