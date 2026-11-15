using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Articles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NormalizedTitle = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CanonicalUrl = table.Column<string>(type: "text", nullable: false),
                    Publisher = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Author = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ContentBody = table.Column<string>(type: "text", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Articles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PerformedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DetailsJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FacebookAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PageName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EncryptedAccessToken = table.Column<string>(type: "text", nullable: false),
                    TokenExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacebookAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SettingValue = table.Column<string>(type: "text", nullable: false),
                    Group = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsEncrypted = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArticleImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalImageUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    LocalTempPath = table.Column<string>(type: "text", nullable: true),
                    Caption = table.Column<string>(type: "text", nullable: true),
                    AltText = table.Column<string>(type: "text", nullable: true),
                    ArticleImageOrder = table.Column<int>(type: "integer", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    ImageHash = table.Column<string>(type: "text", nullable: true),
                    RightsStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArticleImages_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VideoProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Publisher = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TargetDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    ActualDurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    Resolution = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "1080x1920"),
                    AspectRatio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "9:16"),
                    Fps = table.Column<int>(type: "integer", nullable: false),
                    OutputVideoPath = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoProjects_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FacebookPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacebookAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    FacebookPostId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FacebookVideoId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UploadSessionId = table.Column<string>(type: "text", nullable: true),
                    UploadStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacebookPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacebookPosts_FacebookAccounts_FacebookAccountId",
                        column: x => x.FacebookAccountId,
                        principalTable: "FacebookAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FacebookPosts_VideoProjects_VideoProjectId",
                        column: x => x.VideoProjectId,
                        principalTable: "VideoProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    HangfireJobId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Step = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    MaxRetries = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProgressPercentage = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    WorkerHost = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ErrorDetails = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessingJobs_VideoProjects_VideoProjectId",
                        column: x => x.VideoProjectId,
                        principalTable: "VideoProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VideoAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LocalTempPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Sha256Hash = table.Column<string>(type: "text", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsCleanedUp = table.Column<bool>(type: "boolean", nullable: false),
                    CleanedUpAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoAssets_VideoProjects_VideoProjectId",
                        column: x => x.VideoProjectId,
                        principalTable: "VideoProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VideoReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Decision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FeedbackNotes = table.Column<string>(type: "text", nullable: true),
                    InternalQualityScore = table.Column<int>(type: "integer", nullable: false),
                    SeriousViolationsFound = table.Column<bool>(type: "boolean", nullable: false),
                    ViolationDetails = table.Column<string>(type: "text", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoReviews_VideoProjects_VideoProjectId",
                        column: x => x.VideoProjectId,
                        principalTable: "VideoProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VideoScenes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SceneNumber = table.Column<int>(type: "integer", nullable: false),
                    StartTimeSeconds = table.Column<double>(type: "double precision", nullable: false),
                    EndTimeSeconds = table.Column<double>(type: "double precision", nullable: false),
                    VoiceText = table.Column<string>(type: "text", nullable: false),
                    OnScreenText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SelectedArticleImageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImageCaption = table.Column<string>(type: "text", nullable: true),
                    MotionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TransitionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ScenePurpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoScenes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoScenes_ArticleImages_SelectedArticleImageId",
                        column: x => x.SelectedArticleImageId,
                        principalTable: "ArticleImages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VideoScenes_VideoProjects_VideoProjectId",
                        column: x => x.VideoProjectId,
                        principalTable: "VideoProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VideoScripts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Hook = table.Column<string>(type: "text", nullable: false),
                    VoiceOverScript = table.Column<string>(type: "text", nullable: false),
                    KeyFacts = table.Column<string>(type: "text", nullable: false),
                    Context = table.Column<string>(type: "text", nullable: false),
                    Analysis = table.Column<string>(type: "text", nullable: false),
                    Conclusion = table.Column<string>(type: "text", nullable: false),
                    SourceAttribution = table.Column<string>(type: "text", nullable: false),
                    AiProvider = table.Column<string>(type: "text", nullable: false),
                    ModelName = table.Column<string>(type: "text", nullable: false),
                    TokenUsage = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoScripts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoScripts_VideoProjects_VideoProjectId",
                        column: x => x.VideoProjectId,
                        principalTable: "VideoProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VideoMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacebookPostId = table.Column<Guid>(type: "uuid", nullable: false),
                    Views = table.Column<long>(type: "bigint", nullable: false),
                    Reach = table.Column<long>(type: "bigint", nullable: false),
                    Engagement = table.Column<long>(type: "bigint", nullable: false),
                    Shares = table.Column<long>(type: "bigint", nullable: false),
                    Comments = table.Column<long>(type: "bigint", nullable: false),
                    WatchTimeSeconds = table.Column<double>(type: "double precision", nullable: false),
                    CollectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoMetrics_FacebookPosts_FacebookPostId",
                        column: x => x.FacebookPostId,
                        principalTable: "FacebookPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleImages_ArticleId",
                table: "ArticleImages",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleImages_RightsStatus",
                table: "ArticleImages",
                column: "RightsStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_CreatedAtUtc",
                table: "Articles",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_Status",
                table: "Articles",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_Url",
                table: "Articles",
                column: "Url");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CreatedAtUtc",
                table: "AuditLogs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityName_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityName", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_FacebookAccounts_PageId",
                table: "FacebookAccounts",
                column: "PageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacebookPosts_FacebookAccountId",
                table: "FacebookPosts",
                column: "FacebookAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FacebookPosts_FacebookPostId",
                table: "FacebookPosts",
                column: "FacebookPostId");

            migrationBuilder.CreateIndex(
                name: "IX_FacebookPosts_FacebookVideoId",
                table: "FacebookPosts",
                column: "FacebookVideoId");

            migrationBuilder.CreateIndex(
                name: "IX_FacebookPosts_VideoProjectId",
                table: "FacebookPosts",
                column: "VideoProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_HangfireJobId",
                table: "ProcessingJobs",
                column: "HangfireJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_IdempotencyKey",
                table: "ProcessingJobs",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_Status",
                table: "ProcessingJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_VideoProjectId",
                table: "ProcessingJobs",
                column: "VideoProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_SettingKey",
                table: "SystemSettings",
                column: "SettingKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoAssets_ExpiresAtUtc",
                table: "VideoAssets",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_VideoAssets_IsCleanedUp",
                table: "VideoAssets",
                column: "IsCleanedUp");

            migrationBuilder.CreateIndex(
                name: "IX_VideoAssets_VideoProjectId",
                table: "VideoAssets",
                column: "VideoProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoMetrics_CollectedAtUtc",
                table: "VideoMetrics",
                column: "CollectedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_VideoMetrics_FacebookPostId",
                table: "VideoMetrics",
                column: "FacebookPostId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoProjects_ArticleId",
                table: "VideoProjects",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoProjects_CreatedAtUtc",
                table: "VideoProjects",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_VideoProjects_SourceUrl",
                table: "VideoProjects",
                column: "SourceUrl");

            migrationBuilder.CreateIndex(
                name: "IX_VideoProjects_Status",
                table: "VideoProjects",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_VideoReviews_Decision",
                table: "VideoReviews",
                column: "Decision");

            migrationBuilder.CreateIndex(
                name: "IX_VideoReviews_VideoProjectId",
                table: "VideoReviews",
                column: "VideoProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoScenes_SceneNumber",
                table: "VideoScenes",
                column: "SceneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_VideoScenes_SelectedArticleImageId",
                table: "VideoScenes",
                column: "SelectedArticleImageId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoScenes_VideoProjectId",
                table: "VideoScenes",
                column: "VideoProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoScripts_VideoProjectId",
                table: "VideoScripts",
                column: "VideoProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "ProcessingJobs");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "VideoAssets");

            migrationBuilder.DropTable(
                name: "VideoMetrics");

            migrationBuilder.DropTable(
                name: "VideoReviews");

            migrationBuilder.DropTable(
                name: "VideoScenes");

            migrationBuilder.DropTable(
                name: "VideoScripts");

            migrationBuilder.DropTable(
                name: "FacebookPosts");

            migrationBuilder.DropTable(
                name: "ArticleImages");

            migrationBuilder.DropTable(
                name: "FacebookAccounts");

            migrationBuilder.DropTable(
                name: "VideoProjects");

            migrationBuilder.DropTable(
                name: "Articles");
        }
    }
}
