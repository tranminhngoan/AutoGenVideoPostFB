using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoPost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutoPostRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsManual = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    TrendingArticleId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClusterId = table.Column<Guid>(type: "uuid", nullable: true),
                    ArticleUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ArticleTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SourceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Region = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    HotScore = table.Column<double>(type: "double precision", nullable: true),
                    VideoProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    FacebookPostUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoPostRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AutoPostSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    PostsPerDay = table.Column<int>(type: "integer", nullable: false),
                    TimeSlots = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RegionMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Voice = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VoiceSpeed = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AiProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TargetDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    FacebookAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeadMinutes = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoPostSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutoPostRuns_ScheduledAtUtc",
                table: "AutoPostRuns",
                column: "ScheduledAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AutoPostRuns_SlotKey",
                table: "AutoPostRuns",
                column: "SlotKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutoPostRuns_Status",
                table: "AutoPostRuns",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutoPostRuns");

            migrationBuilder.DropTable(
                name: "AutoPostSettings");
        }
    }
}
