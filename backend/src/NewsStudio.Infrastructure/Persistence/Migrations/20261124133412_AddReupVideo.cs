using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReupVideo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReupBackdrops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Tags = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BlobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ThumbBlobId = table.Column<Guid>(type: "uuid", nullable: true),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    SourceName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Author = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LicenseName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LicenseUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DownloadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UseCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReupBackdrops", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReupCharacters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ImageBlobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ThumbBlobId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConsentConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RejectReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FaceScore = table.Column<double>(type: "double precision", nullable: true),
                    FaceBoxJson = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EmbeddingJson = table.Column<string>(type: "text", nullable: true),
                    CheckedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReupCharacters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReupJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SourceBlobId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceThumbBlobId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SourceSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    SourceDurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    SourceFps = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceWidth = table.Column<int>(type: "integer", nullable: false),
                    SourceHeight = table.Column<int>(type: "integer", nullable: false),
                    SourceHasAudio = table.Column<bool>(type: "boolean", nullable: false),
                    RightsConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PeopleConsentConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    RightsNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FaceSwapEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: true),
                    BackgroundEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SegmentsJson = table.Column<string>(type: "text", nullable: false),
                    PlacementJson = table.Column<string>(type: "text", nullable: false),
                    AudioMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AudioBlobId = table.Column<Guid>(type: "uuid", nullable: true),
                    AudioFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    AudioVolume = table.Column<double>(type: "double precision", nullable: false),
                    OriginalVolume = table.Column<double>(type: "double precision", nullable: false),
                    OutputShortSide = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PreviewRun = table.Column<bool>(type: "boolean", nullable: false),
                    Stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ProgressText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    FramesDone = table.Column<int>(type: "integer", nullable: false),
                    FramesTotal = table.Column<int>(type: "integer", nullable: false),
                    Fps = table.Column<double>(type: "double precision", nullable: true),
                    CancelRequested = table.Column<bool>(type: "boolean", nullable: false),
                    HangfireJobId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    WorkerMachine = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PreviewBlobId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutputBlobId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutputDurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    OutputSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    OutputWidth = table.Column<int>(type: "integer", nullable: true),
                    OutputHeight = table.Column<int>(type: "integer", nullable: true),
                    QcReportJson = table.Column<string>(type: "text", nullable: true),
                    StatsJson = table.Column<string>(type: "text", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FilesDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReupJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReupBackdrops_Category",
                table: "ReupBackdrops",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_ReupCharacters_CreatedAtUtc",
                table: "ReupCharacters",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReupJobs_CreatedAtUtc",
                table: "ReupJobs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReupJobs_Status",
                table: "ReupJobs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReupBackdrops");

            migrationBuilder.DropTable(
                name: "ReupCharacters");

            migrationBuilder.DropTable(
                name: "ReupJobs");
        }
    }
}
