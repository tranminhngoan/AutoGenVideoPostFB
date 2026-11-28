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
    DbSet<PageFollowerSnapshot> PageFollowerSnapshots { get; }
    DbSet<PostComment> PostComments { get; }
    DbSet<AiUsage> AiUsages { get; }

    // AI Film Factory Entities
    DbSet<FilmPage> FilmPages { get; }
    DbSet<FilmProject> FilmProjects { get; }
    DbSet<FilmStoryBible> FilmStoryBibles { get; }
    DbSet<FilmCharacter> FilmCharacters { get; }
    DbSet<FilmCharacterReference> FilmCharacterReferences { get; }
    DbSet<FilmLocation> FilmLocations { get; }
    DbSet<FilmContinuityRecord> FilmContinuityRecords { get; }
    DbSet<FilmEpisode> FilmEpisodes { get; }
    DbSet<FilmScene> FilmScenes { get; }
    DbSet<FilmShot> FilmShots { get; }
    DbSet<FilmCostRecord> FilmCostRecords { get; }
    DbSet<FilmPublishingRecord> FilmPublishingRecords { get; }

    // AI video studio (Grok Imagine video)
    DbSet<StudioBlob> StudioBlobs { get; }
    DbSet<StudioAsset> StudioAssets { get; }
    DbSet<StudioMusicTrack> StudioMusicTracks { get; }
    DbSet<StudioVideo> StudioVideos { get; }
    DbSet<StudioShot> StudioShots { get; }
    DbSet<StudioCostRecord> StudioCostRecords { get; }

    // AI Video Studio (analysis on the owner's PC, clips by Google Veo)
    DbSet<AiStudioProject> AiStudioProjects { get; }
    DbSet<AiStudioScene> AiStudioScenes { get; }
    DbSet<AiStudioPromptVersion> AiStudioPromptVersions { get; }
    DbSet<AiStudioGeneration> AiStudioGenerations { get; }

    // Chiến dịch tăng trưởng (Meta Growth Campaign)
    DbSet<GrowthCampaign> GrowthCampaigns { get; }
    DbSet<GrowthScheduledContent> GrowthScheduledContents { get; }
    DbSet<GrowthContentAsset> GrowthContentAssets { get; }
    DbSet<GrowthDailyMetric> GrowthDailyMetrics { get; }
    DbSet<GrowthPostMetric> GrowthPostMetrics { get; }
    DbSet<GrowthCompetitor> GrowthCompetitors { get; }
    DbSet<GrowthCompetitorSnapshot> GrowthCompetitorSnapshots { get; }
    DbSet<GrowthCampaignReport> GrowthCampaignReports { get; }
    DbSet<GrowthActivityLog> GrowthActivityLogs { get; }
    DbSet<GrowthAutopilot> GrowthAutopilots { get; }
    DbSet<GrowthScript> GrowthScripts { get; }
    DbSet<GrowthScriptUsage> GrowthScriptUsages { get; }
    DbSet<GrowthNewsUsage> GrowthNewsUsages { get; }
    DbSet<LandingEvent> LandingEvents { get; }
    DbSet<OutreachSettings> OutreachSettings { get; }
    DbSet<OutreachUnlockAttempt> OutreachUnlockAttempts { get; }
    DbSet<OutreachGroup> OutreachGroups { get; }
    DbSet<OutreachContent> OutreachContents { get; }
    DbSet<OutreachCampaign> OutreachCampaigns { get; }
    DbSet<OutreachActivity> OutreachActivities { get; }
    DbSet<OutreachPostTask> OutreachPostTasks { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
