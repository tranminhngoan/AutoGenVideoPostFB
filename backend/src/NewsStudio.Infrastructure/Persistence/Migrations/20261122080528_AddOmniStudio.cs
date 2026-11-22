using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOmniStudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The hand-written 20261121102500 migration has no [Migration] attribute, so EF never ran it: make the
            // FilmPages.PageNumber index non-unique here, idempotently (works whether or not the index exists).
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_FilmPages_PageNumber"";");

            migrationBuilder.CreateTable(
                name: "StudioAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    BlobId = table.Column<Guid>(type: "uuid", nullable: false),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudioBlobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioBlobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudioCostRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudioVideoId = table.Column<Guid>(type: "uuid", nullable: true),
                    StudioShotId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Seconds = table.Column<double>(type: "double precision", nullable: true),
                    Tokens = table.Column<long>(type: "bigint", nullable: true),
                    CostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioCostRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudioMusicTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Mood = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BlobId = table.Column<Guid>(type: "uuid", nullable: false),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    UseCount = table.Column<int>(type: "integer", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioMusicTracks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudioVideos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    FacebookAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Script = table.Column<string>(type: "text", nullable: false),
                    TargetDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    AspectRatio = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    StyleNotes = table.Column<string>(type: "text", nullable: true),
                    AssetIdsJson = table.Column<string>(type: "text", nullable: false),
                    MusicMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MusicTrackId = table.Column<Guid>(type: "uuid", nullable: true),
                    MusicVolume = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Progress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PlanNotes = table.Column<string>(type: "text", nullable: true),
                    Caption = table.Column<string>(type: "text", nullable: true),
                    FinalBlobId = table.Column<Guid>(type: "uuid", nullable: true),
                    FinalDurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ActualCostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    PublishedPostId = table.Column<string>(type: "text", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioVideos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudioShots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudioVideoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Camera = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Speaker = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Dialogue = table.Column<string>(type: "text", nullable: true),
                    Sound = table.Column<string>(type: "text", nullable: true),
                    AssetIdsJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ClipBlobId = table.Column<Guid>(type: "uuid", nullable: true),
                    InteractionId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioShots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudioShots_StudioVideos_StudioVideoId",
                        column: x => x.StudioVideoId,
                        principalTable: "StudioVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_FilmPages_PageNumber"" ON ""FilmPages"" (""PageNumber"");");

            migrationBuilder.CreateIndex(
                name: "IX_StudioCostRecords_CreatedAtUtc",
                table: "StudioCostRecords",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StudioCostRecords_StudioVideoId",
                table: "StudioCostRecords",
                column: "StudioVideoId");

            migrationBuilder.CreateIndex(
                name: "IX_StudioShots_StudioVideoId_Index",
                table: "StudioShots",
                columns: new[] { "StudioVideoId", "Index" });

            migrationBuilder.CreateIndex(
                name: "IX_StudioVideos_CreatedAtUtc",
                table: "StudioVideos",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudioAssets");

            migrationBuilder.DropTable(
                name: "StudioBlobs");

            migrationBuilder.DropTable(
                name: "StudioCostRecords");

            migrationBuilder.DropTable(
                name: "StudioMusicTracks");

            migrationBuilder.DropTable(
                name: "StudioShots");

            migrationBuilder.DropTable(
                name: "StudioVideos");

            migrationBuilder.DropIndex(
                name: "IX_FilmPages_PageNumber",
                table: "FilmPages");

            migrationBuilder.CreateIndex(
                name: "IX_FilmPages_PageNumber",
                table: "FilmPages",
                column: "PageNumber",
                unique: true);
        }
    }
}
