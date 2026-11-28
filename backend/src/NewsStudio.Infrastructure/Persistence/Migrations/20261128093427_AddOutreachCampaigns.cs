using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutreachCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UtmContent",
                table: "LandingEvents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OutreachActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacebookAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PostUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DoneAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachActivities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutreachCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    PageIdsJson = table.Column<string>(type: "text", nullable: false),
                    GroupIdsJson = table.Column<string>(type: "text", nullable: false),
                    TargetPath = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContentText = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Owner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ResultNote = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachCampaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutreachContents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    Style = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Topic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Origin = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CopiedCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachContents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutreachGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Key = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Topic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExchangeTypes = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Fit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Privacy = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Members = table.Column<int>(type: "integer", nullable: true),
                    Rules = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutreachPostTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: true),
                    PostUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PostText = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    SuggestedComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DoneAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachPostTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutreachSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SessionHours = table.Column<int>(type: "integer", nullable: false),
                    SessionVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutreachUnlockAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachUnlockAttempts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutreachActivities_CampaignId",
                table: "OutreachActivities",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_OutreachCampaigns_Slug",
                table: "OutreachCampaigns",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutreachGroups_Key",
                table: "OutreachGroups",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutreachGroups_Url",
                table: "OutreachGroups",
                column: "Url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutreachPostTasks_Status_CreatedAtUtc",
                table: "OutreachPostTasks",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OutreachUnlockAttempts_Ip_CreatedAtUtc",
                table: "OutreachUnlockAttempts",
                columns: new[] { "Ip", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutreachActivities");

            migrationBuilder.DropTable(
                name: "OutreachCampaigns");

            migrationBuilder.DropTable(
                name: "OutreachContents");

            migrationBuilder.DropTable(
                name: "OutreachGroups");

            migrationBuilder.DropTable(
                name: "OutreachPostTasks");

            migrationBuilder.DropTable(
                name: "OutreachSettings");

            migrationBuilder.DropTable(
                name: "OutreachUnlockAttempts");

            migrationBuilder.DropColumn(
                name: "UtmContent",
                table: "LandingEvents");
        }
    }
}
