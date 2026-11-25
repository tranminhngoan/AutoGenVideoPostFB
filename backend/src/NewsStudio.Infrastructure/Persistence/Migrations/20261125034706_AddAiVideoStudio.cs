using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiVideoStudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The old Reup Video tables are left in place on purpose (no data is deleted); the code no longer uses them.

            migrationBuilder.CreateTable(
                name: "AiStudioGenerations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SceneId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    AspectRatio = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Seconds = table.Column<int>(type: "integer", nullable: false),
                    Seed = table.Column<int>(type: "integer", nullable: true),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    ImageSource = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ParamsJson = table.Column<string>(type: "text", nullable: false),
                    ProviderOperation = table.Column<string>(type: "text", nullable: true),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ClipBlobId = table.Column<Guid>(type: "uuid", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FilesDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiStudioGenerations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiStudioProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceBlobId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceThumbBlobId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SourceContainer = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SourceSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    FpsText = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Fps = table.Column<double>(type: "double precision", nullable: false),
                    VideoCodec = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    BitrateKbps = table.Column<long>(type: "bigint", nullable: true),
                    AspectRatio = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    HasAudio = table.Column<bool>(type: "boolean", nullable: false),
                    AudioJson = table.Column<string>(type: "text", nullable: false),
                    SourceDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    AnalysisModel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ProgressText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    ScenesDone = table.Column<int>(type: "integer", nullable: false),
                    ScenesTotal = table.Column<int>(type: "integer", nullable: false),
                    CancelRequested = table.Column<bool>(type: "boolean", nullable: false),
                    HangfireJobId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    WorkerMachine = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AnalysisStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AnalysisFinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AnalysisMetaJson = table.Column<string>(type: "text", nullable: false),
                    OverviewJson = table.Column<string>(type: "text", nullable: false),
                    CharacterBibleJson = table.Column<string>(type: "text", nullable: false),
                    EnvironmentBibleJson = table.Column<string>(type: "text", nullable: false),
                    StyleBibleJson = table.Column<string>(type: "text", nullable: false),
                    PaletteJson = table.Column<string>(type: "text", nullable: false),
                    TranscriptJson = table.Column<string>(type: "text", nullable: true),
                    PromptMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VariationJson = table.Column<string>(type: "text", nullable: false),
                    GlobalPrompt = table.Column<string>(type: "text", nullable: false),
                    NegativePrompt = table.Column<string>(type: "text", nullable: false),
                    VideoModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    GenAspectRatio = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Seed = table.Column<int>(type: "integer", nullable: true),
                    Continuity = table.Column<bool>(type: "boolean", nullable: false),
                    ReferenceImageBlobId = table.Column<Guid>(type: "uuid", nullable: true),
                    FinalBlobId = table.Column<Guid>(type: "uuid", nullable: true),
                    FinalStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    FinalError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FinalUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiStudioProjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiStudioPromptVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SceneId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiStudioPromptVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiStudioScenes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    StartSeconds = table.Column<double>(type: "double precision", nullable: false),
                    EndSeconds = table.Column<double>(type: "double precision", nullable: false),
                    IsContinuation = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    TargetSeconds = table.Column<int>(type: "integer", nullable: false),
                    ThumbBlobId = table.Column<Guid>(type: "uuid", nullable: true),
                    FramesJson = table.Column<string>(type: "text", nullable: false),
                    MeasuredJson = table.Column<string>(type: "text", nullable: false),
                    AnalysisJson = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    SummaryVi = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Transcript = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    PromptVersion = table.Column<int>(type: "integer", nullable: false),
                    PromptEdited = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiStudioScenes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiStudioScenes_AiStudioProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "AiStudioProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiStudioGenerations_BatchId",
                table: "AiStudioGenerations",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_AiStudioGenerations_ProjectId_CreatedAtUtc",
                table: "AiStudioGenerations",
                columns: new[] { "ProjectId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiStudioGenerations_RequestKey",
                table: "AiStudioGenerations",
                column: "RequestKey");

            migrationBuilder.CreateIndex(
                name: "IX_AiStudioGenerations_SceneId",
                table: "AiStudioGenerations",
                column: "SceneId");

            migrationBuilder.CreateIndex(
                name: "IX_AiStudioGenerations_Status",
                table: "AiStudioGenerations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AiStudioProjects_CreatedAtUtc",
                table: "AiStudioProjects",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiStudioProjects_Status",
                table: "AiStudioProjects",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AiStudioPromptVersions_ProjectId_SceneId_Version",
                table: "AiStudioPromptVersions",
                columns: new[] { "ProjectId", "SceneId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_AiStudioScenes_ProjectId_Order",
                table: "AiStudioScenes",
                columns: new[] { "ProjectId", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiStudioGenerations");

            migrationBuilder.DropTable(
                name: "AiStudioPromptVersions");

            migrationBuilder.DropTable(
                name: "AiStudioScenes");

            migrationBuilder.DropTable(
                name: "AiStudioProjects");
        }
    }
}
