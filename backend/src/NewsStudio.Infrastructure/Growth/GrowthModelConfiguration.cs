using Microsoft.EntityFrameworkCore;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Infrastructure.Growth;

/// <summary>EF mapping of the growth-campaign tables (kept out of ApplicationDbContext so the module stays separate).</summary>
public static class GrowthModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GrowthCampaign>(e =>
        {
            e.ToTable("GrowthCampaigns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.PageName).HasMaxLength(300);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.TimeSlots).HasMaxLength(200);
            e.Property(x => x.ApprovalMode).HasMaxLength(20);
            e.Property(x => x.MetaOneCurrency).HasMaxLength(10);
            e.Property(x => x.AiBudgetUsd).HasPrecision(18, 6);
            e.Property(x => x.AiSpentUsd).HasPrecision(18, 6);
            e.Property(x => x.MetaOneCost).HasPrecision(18, 2);
            e.Property(x => x.LastSyncError).HasMaxLength(2000);
            e.Property(x => x.Notes).HasMaxLength(4000);
            // removing a Page in Settings must keep working: the campaign keeps its history, unlinked
            e.HasOne(x => x.FacebookAccount).WithMany().HasForeignKey(x => x.FacebookAccountId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.FacebookAccountId, x.Status });
        });

        modelBuilder.Entity<GrowthScheduledContent>(e =>
        {
            e.ToTable("GrowthScheduledContents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Caption).HasMaxLength(5000);
            e.Property(x => x.Hashtags).HasMaxLength(500);
            e.Property(x => x.Style).HasMaxLength(30);
            e.Property(x => x.Cta).HasMaxLength(500);
            e.Property(x => x.Topic).HasMaxLength(200);
            e.Property(x => x.SourceType).HasMaxLength(30);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.LastError).HasMaxLength(2000);
            e.Property(x => x.ErrorKind).HasMaxLength(20);
            e.Property(x => x.ExternalPostId).HasMaxLength(100);
            e.Property(x => x.ExternalVideoId).HasMaxLength(100);
            e.Property(x => x.PermalinkUrl).HasMaxLength(500);
            e.HasOne(x => x.Campaign).WithMany(c => c.Contents).HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => new { x.Status, x.ScheduledAtUtc });
            e.HasIndex(x => new { x.CampaignId, x.ScheduledAtUtc });
        });

        modelBuilder.Entity<GrowthContentAsset>(e =>
        {
            e.ToTable("GrowthContentAssets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(10);
            e.Property(x => x.FileName).HasMaxLength(255);
            e.Property(x => x.MimeType).HasMaxLength(50);
            e.Property(x => x.Source).HasMaxLength(20);
            e.HasOne(x => x.Campaign).WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CampaignId);
        });

        modelBuilder.Entity<GrowthDailyMetric>(e =>
        {
            e.ToTable("GrowthDailyMetrics");
            e.HasKey(x => x.Id);
            e.Property(x => x.ManualNote).HasMaxLength(1000);
            e.HasOne<FacebookAccount>().WithMany().HasForeignKey(x => x.FacebookAccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.FacebookAccountId, x.Date }).IsUnique();
        });

        modelBuilder.Entity<GrowthPostMetric>(e =>
        {
            e.ToTable("GrowthPostMetrics");
            e.HasKey(x => x.Id);
            e.Property(x => x.Error).HasMaxLength(1000);
            e.HasOne(x => x.Content).WithMany(c => c.Metrics).HasForeignKey(x => x.ContentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ContentId, x.Date }).IsUnique();
        });

        modelBuilder.Entity<GrowthCompetitor>(e =>
        {
            e.ToTable("GrowthCompetitors");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Url).HasMaxLength(500);
            e.Property(x => x.Niche).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.HasOne<FacebookAccount>().WithMany().HasForeignKey(x => x.FacebookAccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.FacebookAccountId);
        });

        modelBuilder.Entity<GrowthCompetitorSnapshot>(e =>
        {
            e.ToTable("GrowthCompetitorSnapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.PostUrl).HasMaxLength(500);
            e.Property(x => x.PostTitle).HasMaxLength(500);
            e.Property(x => x.Format).HasMaxLength(20);
            e.Property(x => x.Topic).HasMaxLength(200);
            e.Property(x => x.Note).HasMaxLength(2000);
            e.HasOne(x => x.Competitor).WithMany(c => c.Snapshots).HasForeignKey(x => x.CompetitorId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.CompetitorId, x.Date });
        });

        modelBuilder.Entity<GrowthCampaignReport>(e =>
        {
            e.ToTable("GrowthCampaignReports");
            e.HasKey(x => x.Id);
            e.Property(x => x.Verdict).HasMaxLength(20);
            e.Property(x => x.AiCommentary).HasMaxLength(8000);
            e.HasOne(x => x.Campaign).WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.CampaignId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<GrowthActivityLog>(e =>
        {
            e.ToTable("GrowthActivityLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Level).HasMaxLength(10);
            e.Property(x => x.Action).HasMaxLength(50);
            e.Property(x => x.Message).HasMaxLength(2000);
            e.Property(x => x.Actor).HasMaxLength(20);
            e.HasOne<GrowthCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.CampaignId, x.CreatedAtUtc });
        });
    }
}
