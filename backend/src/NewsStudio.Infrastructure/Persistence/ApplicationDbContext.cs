using Microsoft.EntityFrameworkCore;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Domain.Common;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Article> Articles => Set<Article>();
    public DbSet<ArticleImage> ArticleImages => Set<ArticleImage>();
    public DbSet<VideoProject> VideoProjects => Set<VideoProject>();
    public DbSet<VideoScene> VideoScenes => Set<VideoScene>();
    public DbSet<VideoScript> VideoScripts => Set<VideoScript>();
    public DbSet<VideoAsset> VideoAssets => Set<VideoAsset>();
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
    public DbSet<VideoReview> VideoReviews => Set<VideoReview>();
    public DbSet<FacebookAccount> FacebookAccounts => Set<FacebookAccount>();
    public DbSet<FacebookPost> FacebookPosts => Set<FacebookPost>();
    public DbSet<VideoMetric> VideoMetrics => Set<VideoMetric>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<StudioBlob> StudioBlobs => Set<StudioBlob>();
    public DbSet<StudioAsset> StudioAssets => Set<StudioAsset>();
    public DbSet<StudioMusicTrack> StudioMusicTracks => Set<StudioMusicTrack>();
    public DbSet<StudioVideo> StudioVideos => Set<StudioVideo>();
    public DbSet<StudioShot> StudioShots => Set<StudioShot>();
    public DbSet<StudioCostRecord> StudioCostRecords => Set<StudioCostRecord>();
    public DbSet<AiStudioProject> AiStudioProjects => Set<AiStudioProject>();
    public DbSet<AiStudioScene> AiStudioScenes => Set<AiStudioScene>();
    public DbSet<AiStudioPromptVersion> AiStudioPromptVersions => Set<AiStudioPromptVersion>();
    public DbSet<AiStudioGeneration> AiStudioGenerations => Set<AiStudioGeneration>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TrendingArticle> TrendingArticles => Set<TrendingArticle>();
    public DbSet<AutoPostSettings> AutoPostSettings => Set<AutoPostSettings>();
    public DbSet<AutoPostRun> AutoPostRuns => Set<AutoPostRun>();
    public DbSet<DailyDigestRun> DailyDigestRuns => Set<DailyDigestRun>();
    public DbSet<InstagramMetric> InstagramMetrics => Set<InstagramMetric>();
    public DbSet<PageFollowerSnapshot> PageFollowerSnapshots => Set<PageFollowerSnapshot>();
    public DbSet<PostComment> PostComments => Set<PostComment>();
    public DbSet<AiUsage> AiUsages => Set<AiUsage>();

    // AI Film Factory DbSets
    public DbSet<FilmPage> FilmPages => Set<FilmPage>();
    public DbSet<FilmProject> FilmProjects => Set<FilmProject>();
    public DbSet<FilmStoryBible> FilmStoryBibles => Set<FilmStoryBible>();
    public DbSet<FilmCharacter> FilmCharacters => Set<FilmCharacter>();
    public DbSet<FilmCharacterReference> FilmCharacterReferences => Set<FilmCharacterReference>();
    public DbSet<FilmLocation> FilmLocations => Set<FilmLocation>();
    public DbSet<FilmContinuityRecord> FilmContinuityRecords => Set<FilmContinuityRecord>();
    public DbSet<FilmEpisode> FilmEpisodes => Set<FilmEpisode>();
    public DbSet<FilmScene> FilmScenes => Set<FilmScene>();
    public DbSet<FilmShot> FilmShots => Set<FilmShot>();
    public DbSet<FilmCostRecord> FilmCostRecords => Set<FilmCostRecord>();
    public DbSet<FilmPublishingRecord> FilmPublishingRecords => Set<FilmPublishingRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // "Tự động đăng"
        modelBuilder.Entity<AutoPostSettings>(entity =>
        {
            entity.ToTable("AutoPostSettings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TimeSlots).HasMaxLength(500).IsRequired();
            entity.Property(e => e.RegionMode).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Voice).HasMaxLength(100);
            entity.Property(e => e.VoiceSpeed).HasMaxLength(20);
            entity.Property(e => e.AiProvider).HasMaxLength(50);
            entity.Property(e => e.Topic).HasMaxLength(50).IsRequired();
            entity.Property(e => e.TopicKeywords).HasMaxLength(1000);
            entity.Property(e => e.DigestTime).HasMaxLength(5).IsRequired();
            entity.Property(e => e.WritingTone).HasMaxLength(20).IsRequired();
            entity.HasIndex(e => e.FacebookAccountId);
        });

        modelBuilder.Entity<VideoProject>().Property(e => e.GateStatus).HasMaxLength(20).IsRequired();
        modelBuilder.Entity<ArticleImage>(entity =>
        {
            entity.Property(e => e.ImageType).HasMaxLength(20);
            entity.Property(e => e.SourceName).HasMaxLength(200);
            entity.Property(e => e.SourcePageUrl).HasMaxLength(2000);
            entity.Property(e => e.LicenseType).HasMaxLength(100);
            entity.Property(e => e.LicenseEvidence).HasMaxLength(2000);
            entity.Property(e => e.SubjectName).HasMaxLength(300);
            entity.Property(e => e.SubjectType).HasMaxLength(2);
            entity.Property(e => e.VerificationStatus).HasMaxLength(20);
        });

        modelBuilder.Entity<AiUsage>(entity =>
        {
            entity.ToTable("AiUsages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Model).HasMaxLength(100).IsRequired();
            entity.Property(e => e.CostUsd).HasPrecision(12, 6);
            entity.HasIndex(e => e.CreatedAtUtc);
        });

        modelBuilder.Entity<InstagramMetric>(entity =>
        {
            entity.ToTable("InstagramMetrics");
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.FacebookPost)
                  .WithMany(p => p.InstagramMetrics)
                  .HasForeignKey(e => e.FacebookPostId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.FacebookPostId, e.CollectedAtUtc });
        });

        modelBuilder.Entity<DailyDigestRun>(entity =>
        {
            entity.ToTable("DailyDigestRuns");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DigestDate).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Kind).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.WorkFolder).HasMaxLength(500);
            entity.Property(e => e.PostId).HasMaxLength(100);
            entity.Property(e => e.PostError).HasMaxLength(1000);
            entity.Property(e => e.StoryPostId).HasMaxLength(100);
            entity.Property(e => e.StoryError).HasMaxLength(1000);
            entity.Property(e => e.Message).HasMaxLength(1000);
            entity.HasIndex(e => new { e.FacebookAccountId, e.DigestDate });
        });

        modelBuilder.Entity<AutoPostRun>(entity =>
        {
            entity.ToTable("AutoPostRuns");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SlotKey).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.ArticleUrl).HasMaxLength(2000);
            entity.Property(e => e.ArticleTitle).HasMaxLength(500);
            entity.Property(e => e.SourceName).HasMaxLength(100);
            entity.Property(e => e.Region).HasMaxLength(10);
            entity.Property(e => e.FacebookPostUrl).HasMaxLength(500);
            entity.HasIndex(e => e.SlotKey).IsUnique();
            entity.HasIndex(e => e.ScheduledAtUtc);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.FacebookAccountId);
        });

        // TrendingArticle Configuration ("Tin đang hot")
        modelBuilder.Entity<TrendingArticle>(entity =>
        {
            entity.ToTable("TrendingArticles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Url).HasMaxLength(2000).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ImageUrl).HasMaxLength(2000);
            entity.Property(e => e.SourceKey).HasMaxLength(50).IsRequired();
            entity.Property(e => e.SourceName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Region).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Language).HasMaxLength(10).IsRequired();
            entity.Property(e => e.TrendKeyword).HasMaxLength(200);
            entity.Property(e => e.Topics).HasMaxLength(300);
            entity.HasIndex(e => e.Url).IsUnique();
            entity.HasIndex(e => e.PublishedAtUtc);
            entity.HasIndex(e => e.ClusterId);
        });

        // Article Configuration
        modelBuilder.Entity<Article>(entity =>
        {
            entity.ToTable("Articles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Url).HasMaxLength(2000).IsRequired();
            entity.Property(e => e.Publisher).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.HasIndex(e => e.Url);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => e.Status);
        });

        // ArticleImage Configuration
        modelBuilder.Entity<ArticleImage>(entity =>
        {
            entity.ToTable("ArticleImages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OriginalImageUrl).HasMaxLength(2000).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.RightsStatus).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Category).HasConversion<string>().HasMaxLength(50);

            entity.HasOne(e => e.Article)
                  .WithMany(a => a.Images)
                  .HasForeignKey(e => e.ArticleId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.ArticleId);
            entity.HasIndex(e => e.RightsStatus);
        });

        // VideoProject Configuration
        modelBuilder.Entity<VideoProject>(entity =>
        {
            entity.Property(e => e.Topic).HasMaxLength(50);
            entity.ToTable("VideoProjects");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.SourceUrl).HasMaxLength(2000).IsRequired();
            entity.Property(e => e.Publisher).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Resolution).HasMaxLength(20).HasDefaultValue("1080x1920");
            entity.Property(e => e.AspectRatio).HasMaxLength(20).HasDefaultValue("9:16");

            entity.HasOne(e => e.Article)
                  .WithMany(a => a.VideoProjects)
                  .HasForeignKey(e => e.ArticleId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => e.SourceUrl);
        });

        // VideoScript Configuration
        modelBuilder.Entity<VideoScript>(entity =>
        {
            entity.ToTable("VideoScripts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50);

            entity.HasOne(e => e.VideoProject)
                  .WithOne(p => p.Script)
                  .HasForeignKey<VideoScript>(e => e.VideoProjectId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.VideoProjectId);
        });

        // VideoScene Configuration
        modelBuilder.Entity<VideoScene>(entity =>
        {
            entity.ToTable("VideoScenes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.VoiceText).IsRequired();
            entity.Property(e => e.OnScreenText).HasMaxLength(500).IsRequired();
            entity.Property(e => e.MotionType).HasMaxLength(50);
            entity.Property(e => e.TransitionType).HasMaxLength(50);
            entity.Property(e => e.ScenePurpose).HasMaxLength(50);
            entity.Property(e => e.Status).HasMaxLength(50);

            entity.HasOne(e => e.VideoProject)
                  .WithMany(p => p.Scenes)
                  .HasForeignKey(e => e.VideoProjectId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SelectedArticleImage)
                  .WithMany(i => i.VideoScenes)
                  .HasForeignKey(e => e.SelectedArticleImageId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.VideoProjectId);
            entity.HasIndex(e => e.SceneNumber);
        });

        // VideoAsset Configuration
        modelBuilder.Entity<VideoAsset>(entity =>
        {
            entity.ToTable("VideoAssets");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AssetType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.LocalTempPath).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.MimeType).HasMaxLength(100);
            entity.Property(e => e.Status).HasMaxLength(50);

            entity.HasOne(e => e.VideoProject)
                  .WithMany(p => p.Assets)
                  .HasForeignKey(e => e.VideoProjectId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.VideoProjectId);
            entity.HasIndex(e => e.IsCleanedUp);
            entity.HasIndex(e => e.ExpiresAtUtc);
        });

        // ProcessingJob Configuration
        modelBuilder.Entity<ProcessingJob>(entity =>
        {
            entity.ToTable("ProcessingJobs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.HangfireJobId).HasMaxLength(100);
            entity.Property(e => e.Step).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(100);

            entity.HasOne(e => e.VideoProject)
                  .WithMany(p => p.ProcessingJobs)
                  .HasForeignKey(e => e.VideoProjectId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.VideoProjectId);
            entity.HasIndex(e => e.HangfireJobId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.IdempotencyKey);
        });

        // VideoReview Configuration
        modelBuilder.Entity<VideoReview>(entity =>
        {
            entity.ToTable("VideoReviews");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ReviewedBy).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Decision).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50);

            entity.HasOne(e => e.VideoProject)
                  .WithMany(p => p.Reviews)
                  .HasForeignKey(e => e.VideoProjectId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.VideoProjectId);
            entity.HasIndex(e => e.Decision);
        });

        // FacebookAccount Configuration
        modelBuilder.Entity<FacebookAccount>(entity =>
        {
            entity.ToTable("FacebookAccounts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PageId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PageName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.HasIndex(e => e.PageId).IsUnique();
        });

        // FacebookPost Configuration
        modelBuilder.Entity<FacebookPost>(entity =>
        {
            entity.ToTable("FacebookPosts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FacebookPostId).HasMaxLength(100);
            entity.Property(e => e.FacebookVideoId).HasMaxLength(100);
            entity.Property(e => e.UploadStatus).HasMaxLength(50);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.FirstCommentId).HasMaxLength(100);
            entity.Property(e => e.FirstCommentError).HasMaxLength(1000);
            entity.Property(e => e.InstagramMediaId).HasMaxLength(100);
            entity.Property(e => e.InstagramPermalink).HasMaxLength(300);
            entity.Property(e => e.InstagramError).HasMaxLength(1000);

            entity.HasOne(e => e.VideoProject)
                  .WithMany(p => p.FacebookPosts)
                  .HasForeignKey(e => e.VideoProjectId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FacebookAccount)
                  .WithMany(a => a.FacebookPosts)
                  .HasForeignKey(e => e.FacebookAccountId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.VideoProjectId);
            entity.HasIndex(e => e.FacebookPostId);
            entity.HasIndex(e => e.FacebookVideoId);
            entity.HasIndex(e => e.StudioVideoId);
            entity.HasIndex(e => e.InstagramMediaId);
            entity.Property(e => e.Title).HasMaxLength(300);
            entity.Property(e => e.Source).HasMaxLength(20);
        });

        modelBuilder.Entity<PostComment>(entity =>
        {
            entity.ToTable("PostComments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Network).HasMaxLength(20);
            entity.Property(e => e.ExternalId).HasMaxLength(100);
            entity.Property(e => e.Author).HasMaxLength(200);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.HasOne(e => e.FacebookPost).WithMany().HasForeignKey(e => e.FacebookPostId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.ExternalId).IsUnique();
            entity.HasIndex(e => new { e.Status, e.CommentedAtUtc });
        });

        modelBuilder.Entity<PageFollowerSnapshot>(entity =>
        {
            entity.ToTable("PageFollowerSnapshots");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.FacebookAccountId, e.Date }).IsUnique();
        });

        // VideoMetric Configuration
        modelBuilder.Entity<VideoMetric>(entity =>
        {
            entity.ToTable("VideoMetrics");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasMaxLength(50);

            entity.HasOne(e => e.FacebookPost)
                  .WithMany(p => p.Metrics)
                  .HasForeignKey(e => e.FacebookPostId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.FacebookPostId);
            entity.HasIndex(e => e.CollectedAtUtc);
        });

        // SystemSetting Configuration
        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.ToTable("SystemSettings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SettingKey).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Group).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.HasIndex(e => e.SettingKey).IsUnique();
        });

        // AuditLog Configuration
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EntityName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.EntityId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(50).IsRequired();
            entity.Property(e => e.PerformedBy).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50);

            entity.HasIndex(e => new { e.EntityName, e.EntityId });
            entity.HasIndex(e => e.CreatedAtUtc);
        });

        // ── AI FILM FACTORY CONFIGURATIONS ──
        modelBuilder.Entity<FilmPage>(entity =>
        {
            entity.ToTable("FilmPages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.PrimaryGenre).HasMaxLength(100);
            entity.Property(e => e.VisualArtStyle).HasMaxLength(100);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.HasIndex(e => e.PageNumber);
            entity.HasOne(e => e.LinkedFacebookAccount)
                  .WithMany()
                  .HasForeignKey(e => e.LinkedFacebookAccountId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<FilmProject>(entity =>
        {
            entity.ToTable("FilmProjects");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(300).IsRequired();
            entity.Property(e => e.Genre).HasMaxLength(100);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.TotalBudgetLimitUsd).HasPrecision(10, 4);
            entity.Property(e => e.TotalActualCostUsd).HasPrecision(10, 4);
            entity.HasIndex(e => e.FilmPageId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasOne(e => e.FilmPage)
                  .WithMany(p => p.Projects)
                  .HasForeignKey(e => e.FilmPageId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FilmStoryBible>(entity =>
        {
            entity.ToTable("FilmStoryBibles");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FilmProjectId).IsUnique();
            entity.HasOne(e => e.FilmProject)
                  .WithOne(p => p.StoryBible)
                  .HasForeignKey<FilmStoryBible>(e => e.FilmProjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FilmCharacter>(entity =>
        {
            entity.ToTable("FilmCharacters");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CharacterId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Role).HasMaxLength(50);
            entity.HasIndex(e => new { e.FilmProjectId, e.CharacterId });
            entity.HasOne(e => e.FilmProject)
                  .WithMany(p => p.Characters)
                  .HasForeignKey(e => e.FilmProjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FilmCharacterReference>(entity =>
        {
            entity.ToTable("FilmCharacterReferences");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Angle).HasMaxLength(50);
            entity.HasIndex(e => e.FilmCharacterId);
            entity.HasOne(e => e.FilmCharacter)
                  .WithMany(c => c.References)
                  .HasForeignKey(e => e.FilmCharacterId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FilmLocation>(entity =>
        {
            entity.ToTable("FilmLocations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LocationId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(e => new { e.FilmProjectId, e.LocationId });
            entity.HasOne(e => e.FilmProject)
                  .WithMany(p => p.Locations)
                  .HasForeignKey(e => e.FilmProjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FilmContinuityRecord>(entity =>
        {
            entity.ToTable("FilmContinuityRecords");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.FilmProjectId, e.EpisodeNumber, e.SceneNumber });
            entity.HasOne(e => e.FilmProject)
                  .WithMany(p => p.ContinuityRecords)
                  .HasForeignKey(e => e.FilmProjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FilmEpisode>(entity =>
        {
            entity.ToTable("FilmEpisodes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(300).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.EstimatedCostUsd).HasPrecision(10, 4);
            entity.Property(e => e.ActualCostUsd).HasPrecision(10, 4);
            entity.HasIndex(e => new { e.FilmProjectId, e.EpisodeNumber });
            entity.HasOne(e => e.FilmProject)
                  .WithMany(p => p.Episodes)
                  .HasForeignKey(e => e.FilmProjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FilmScene>(entity =>
        {
            entity.ToTable("FilmScenes");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.FilmEpisodeId, e.SceneNumber });
            entity.HasOne(e => e.FilmEpisode)
                  .WithMany(ep => ep.Scenes)
                  .HasForeignKey(e => e.FilmEpisodeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FilmShot>(entity =>
        {
            entity.ToTable("FilmShots");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ShotType).HasMaxLength(50);
            entity.Property(e => e.CameraMovement).HasMaxLength(50);
            entity.Property(e => e.VisualMethod).HasMaxLength(50);
            entity.Property(e => e.Provider).HasMaxLength(50);
            entity.Property(e => e.RenderStatus).HasMaxLength(50);
            entity.Property(e => e.EstimatedCostUsd).HasPrecision(10, 4);
            entity.Property(e => e.ActualCostUsd).HasPrecision(10, 4);
            entity.HasIndex(e => new { e.FilmSceneId, e.ShotNumber });
            entity.HasIndex(e => e.FilmEpisodeId);
            entity.HasOne(e => e.FilmScene)
                  .WithMany(sc => sc.Shots)
                  .HasForeignKey(e => e.FilmSceneId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.FilmEpisode)
                  .WithMany(ep => ep.Shots)
                  .HasForeignKey(e => e.FilmEpisodeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FilmCostRecord>(entity =>
        {
            entity.ToTable("FilmCostRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Provider).HasMaxLength(50);
            entity.Property(e => e.ServiceType).HasMaxLength(50);
            entity.Property(e => e.CostUsd).HasPrecision(10, 4);
            entity.HasIndex(e => e.FilmProjectId);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasOne(e => e.FilmProject)
                  .WithMany(p => p.CostRecords)
                  .HasForeignKey(e => e.FilmProjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FilmPublishingRecord>(entity =>
        {
            entity.ToTable("FilmPublishingRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Platform).HasMaxLength(50);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.HasIndex(e => new { e.FilmEpisodeId, e.FilmPageId });
            entity.HasOne(e => e.FilmEpisode)
                  .WithMany()
                  .HasForeignKey(e => e.FilmEpisodeId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.FilmPage)
                  .WithMany()
                  .HasForeignKey(e => e.FilmPageId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ───────── AI video studio ─────────
        modelBuilder.Entity<StudioBlob>(entity =>
        {
            entity.ToTable("StudioBlobs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MimeType).HasMaxLength(100);
        });
        modelBuilder.Entity<StudioAsset>(entity =>
        {
            entity.ToTable("StudioAssets");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Kind).HasMaxLength(30);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.MimeType).HasMaxLength(100);
        });
        modelBuilder.Entity<StudioMusicTrack>(entity =>
        {
            entity.ToTable("StudioMusicTracks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Mood).HasMaxLength(300);
            entity.Property(e => e.MimeType).HasMaxLength(100);
        });
        modelBuilder.Entity<StudioVideo>(entity =>
        {
            entity.ToTable("StudioVideos");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(300).IsRequired();
            entity.Property(e => e.AspectRatio).HasMaxLength(10);
            entity.Property(e => e.Resolution).HasMaxLength(10);
            entity.Property(e => e.MusicMode).HasMaxLength(20);
            entity.Property(e => e.Status).HasMaxLength(30);
            entity.Property(e => e.Progress).HasMaxLength(500);
            entity.Property(e => e.EstimatedCostUsd).HasPrecision(18, 6);
            entity.Property(e => e.ActualCostUsd).HasPrecision(18, 6);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasMany(e => e.Shots).WithOne(s => s.StudioVideo!).HasForeignKey(s => s.StudioVideoId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<StudioShot>(entity =>
        {
            entity.ToTable("StudioShots");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasMaxLength(30);
            entity.Property(e => e.Camera).HasMaxLength(500);
            entity.Property(e => e.Speaker).HasMaxLength(200);
            entity.Property(e => e.Location).HasMaxLength(200);
            entity.Property(e => e.CostUsd).HasPrecision(18, 6);
            entity.HasIndex(e => new { e.StudioVideoId, e.Index });
        });
        modelBuilder.Entity<StudioCostRecord>(entity =>
        {
            entity.ToTable("StudioCostRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).HasMaxLength(30);
            entity.Property(e => e.Model).HasMaxLength(100);
            entity.Property(e => e.CostUsd).HasPrecision(18, 6);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => e.StudioVideoId);
        });

        // ───────── AI Video Studio (analysis on the owner's PC, clips by Seedance) ─────────
        modelBuilder.Entity<AiStudioProject>(entity =>
        {
            entity.ToTable("AiStudioProjects");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(300).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.SourceFileName).HasMaxLength(300);
            entity.Property(e => e.SourceContainer).HasMaxLength(10);
            entity.Property(e => e.FpsText).HasMaxLength(30);
            entity.Property(e => e.VideoCodec).HasMaxLength(40);
            entity.Property(e => e.AspectRatio).HasMaxLength(10);
            entity.Property(e => e.AnalysisModel).HasMaxLength(10);
            entity.Property(e => e.Stage).HasMaxLength(40);
            entity.Property(e => e.ProgressText).HasMaxLength(500);
            entity.Property(e => e.HangfireJobId).HasMaxLength(50);
            entity.Property(e => e.WorkerMachine).HasMaxLength(100);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.PromptMode).HasMaxLength(20);
            entity.Property(e => e.Topic).HasMaxLength(30);
            entity.Property(e => e.VideoModel).HasMaxLength(100);
            entity.Property(e => e.Resolution).HasMaxLength(10);
            entity.Property(e => e.GenAspectRatio).HasMaxLength(10);
            entity.Property(e => e.FinalStatus).HasMaxLength(20);
            entity.Property(e => e.FinalError).HasMaxLength(1000);
            entity.HasMany(e => e.Scenes).WithOne(s => s.Project!).HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => e.Status);
        });
        modelBuilder.Entity<AiStudioScene>(entity =>
        {
            entity.ToTable("AiStudioScenes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SummaryVi).HasMaxLength(1000);
            entity.Property(e => e.Transcript).HasMaxLength(2000);
            entity.HasIndex(e => new { e.ProjectId, e.Order });
        });
        modelBuilder.Entity<AiStudioPromptVersion>(entity =>
        {
            entity.ToTable("AiStudioPromptVersions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Source).HasMaxLength(20);
            entity.Property(e => e.Mode).HasMaxLength(20);
            entity.HasIndex(e => new { e.ProjectId, e.SceneId, e.Version });
        });
        modelBuilder.Entity<AiStudioGeneration>(entity =>
        {
            entity.ToTable("AiStudioGenerations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RequestKey).HasMaxLength(80);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.Model).HasMaxLength(100);
            entity.Property(e => e.Resolution).HasMaxLength(10);
            entity.Property(e => e.AspectRatio).HasMaxLength(10);
            entity.Property(e => e.ImageSource).HasMaxLength(30);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.EstimatedCostUsd).HasPrecision(18, 6);
            entity.Property(e => e.CostUsd).HasPrecision(18, 6);
            entity.HasIndex(e => new { e.ProjectId, e.CreatedAtUtc });
            entity.HasIndex(e => e.SceneId);
            entity.HasIndex(e => e.BatchId);
            entity.HasIndex(e => e.RequestKey);
            entity.HasIndex(e => e.Status);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = DateTime.UtcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
