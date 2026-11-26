using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGrowthCampaign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GrowthCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FacebookAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    PageName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TargetFollowers = table.Column<long>(type: "bigint", nullable: true),
                    TargetViews = table.Column<long>(type: "bigint", nullable: true),
                    ReelsPerDay = table.Column<int>(type: "integer", nullable: false),
                    PhotosPerDay = table.Column<int>(type: "integer", nullable: false),
                    StoriesPerDay = table.Column<int>(type: "integer", nullable: false),
                    TimeSlots = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AiBudgetUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    AiSpentUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ApprovalMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartingFollowers = table.Column<long>(type: "bigint", nullable: true),
                    StartingFollowersAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndingFollowers = table.Column<long>(type: "bigint", nullable: true),
                    EndingFollowersAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MetaOneCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MetaOneCurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ChecklistJson = table.Column<string>(type: "text", nullable: false),
                    PlanJson = table.Column<string>(type: "text", nullable: false),
                    MetricAvailabilityJson = table.Column<string>(type: "text", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrowthCampaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrowthCampaigns_FacebookAccounts_FacebookAccountId",
                        column: x => x.FacebookAccountId,
                        principalTable: "FacebookAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "GrowthCompetitors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacebookAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Niche = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrowthCompetitors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrowthCompetitors_FacebookAccounts_FacebookAccountId",
                        column: x => x.FacebookAccountId,
                        principalTable: "FacebookAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GrowthDailyMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacebookAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Followers = table.Column<long>(type: "bigint", nullable: true),
                    NewFollows = table.Column<long>(type: "bigint", nullable: true),
                    Unfollows = table.Column<long>(type: "bigint", nullable: true),
                    PageViews = table.Column<long>(type: "bigint", nullable: true),
                    MediaViews = table.Column<long>(type: "bigint", nullable: true),
                    MediaViewers = table.Column<long>(type: "bigint", nullable: true),
                    Engagements = table.Column<long>(type: "bigint", nullable: true),
                    CollectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ManualInvitesSent = table.Column<long>(type: "bigint", nullable: true),
                    ManualInvitesAccepted = table.Column<long>(type: "bigint", nullable: true),
                    ManualPageVisits = table.Column<long>(type: "bigint", nullable: true),
                    ManualNewFollowers = table.Column<long>(type: "bigint", nullable: true),
                    ManualNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrowthDailyMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrowthDailyMetrics_FacebookAccounts_FacebookAccountId",
                        column: x => x.FacebookAccountId,
                        principalTable: "FacebookAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GrowthActivityLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Level = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Actor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrowthActivityLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrowthActivityLogs_GrowthCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "GrowthCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GrowthCampaignReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    DataJson = table.Column<string>(type: "text", nullable: false),
                    Verdict = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AiCommentary = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrowthCampaignReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrowthCampaignReports_GrowthCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "GrowthCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GrowthContentAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrowthContentAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrowthContentAssets_GrowthCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "GrowthCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GrowthScheduledContents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Caption = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    Hashtags = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Style = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Cta = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssetIdsJson = table.Column<string>(type: "text", nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ManualPublish = table.Column<bool>(type: "boolean", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ErrorKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExternalPostId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalVideoId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PermalinkUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FacebookPostRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedManually = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrowthScheduledContents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrowthScheduledContents_GrowthCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "GrowthCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GrowthCompetitorSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Followers = table.Column<long>(type: "bigint", nullable: true),
                    PostsPerWeek = table.Column<int>(type: "integer", nullable: true),
                    PostUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PostTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Reactions = table.Column<long>(type: "bigint", nullable: true),
                    Comments = table.Column<long>(type: "bigint", nullable: true),
                    Shares = table.Column<long>(type: "bigint", nullable: true),
                    Views = table.Column<long>(type: "bigint", nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrowthCompetitorSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrowthCompetitorSnapshots_GrowthCompetitors_CompetitorId",
                        column: x => x.CompetitorId,
                        principalTable: "GrowthCompetitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GrowthPostMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Views = table.Column<long>(type: "bigint", nullable: true),
                    Reach = table.Column<long>(type: "bigint", nullable: true),
                    Reactions = table.Column<long>(type: "bigint", nullable: true),
                    Comments = table.Column<long>(type: "bigint", nullable: true),
                    Shares = table.Column<long>(type: "bigint", nullable: true),
                    Clicks = table.Column<long>(type: "bigint", nullable: true),
                    Follows = table.Column<long>(type: "bigint", nullable: true),
                    CollectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrowthPostMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrowthPostMetrics_GrowthScheduledContents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "GrowthScheduledContents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GrowthActivityLogs_CampaignId_CreatedAtUtc",
                table: "GrowthActivityLogs",
                columns: new[] { "CampaignId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GrowthCampaignReports_CampaignId_CreatedAtUtc",
                table: "GrowthCampaignReports",
                columns: new[] { "CampaignId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GrowthCampaigns_FacebookAccountId_Status",
                table: "GrowthCampaigns",
                columns: new[] { "FacebookAccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_GrowthCompetitors_FacebookAccountId",
                table: "GrowthCompetitors",
                column: "FacebookAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GrowthCompetitorSnapshots_CompetitorId_Date",
                table: "GrowthCompetitorSnapshots",
                columns: new[] { "CompetitorId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_GrowthContentAssets_CampaignId",
                table: "GrowthContentAssets",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_GrowthDailyMetrics_FacebookAccountId_Date",
                table: "GrowthDailyMetrics",
                columns: new[] { "FacebookAccountId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GrowthPostMetrics_ContentId_Date",
                table: "GrowthPostMetrics",
                columns: new[] { "ContentId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GrowthScheduledContents_CampaignId_ScheduledAtUtc",
                table: "GrowthScheduledContents",
                columns: new[] { "CampaignId", "ScheduledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GrowthScheduledContents_IdempotencyKey",
                table: "GrowthScheduledContents",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GrowthScheduledContents_Status_ScheduledAtUtc",
                table: "GrowthScheduledContents",
                columns: new[] { "Status", "ScheduledAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GrowthActivityLogs");

            migrationBuilder.DropTable(
                name: "GrowthCampaignReports");

            migrationBuilder.DropTable(
                name: "GrowthCompetitorSnapshots");

            migrationBuilder.DropTable(
                name: "GrowthContentAssets");

            migrationBuilder.DropTable(
                name: "GrowthDailyMetrics");

            migrationBuilder.DropTable(
                name: "GrowthPostMetrics");

            migrationBuilder.DropTable(
                name: "GrowthCompetitors");

            migrationBuilder.DropTable(
                name: "GrowthScheduledContents");

            migrationBuilder.DropTable(
                name: "GrowthCampaigns");
        }
    }
}
