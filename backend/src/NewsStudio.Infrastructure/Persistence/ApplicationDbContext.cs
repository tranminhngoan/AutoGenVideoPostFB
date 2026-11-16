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
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TrendingArticle> TrendingArticles => Set<TrendingArticle>();
    public DbSet<AutoPostSettings> AutoPostSettings => Set<AutoPostSettings>();
    public DbSet<AutoPostRun> AutoPostRuns => Set<AutoPostRun>();
    public DbSet<DailyDigestRun> DailyDigestRuns => Set<DailyDigestRun>();

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
            entity.HasIndex(e => e.FacebookAccountId);
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
