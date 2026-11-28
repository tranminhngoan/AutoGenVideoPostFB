using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutreachWallPosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutreachShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WallPostId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacebookAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachShares", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutreachWallPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacebookAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalPostId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PermalinkUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Text = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachWallPosts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutreachShares_CampaignId",
                table: "OutreachShares",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_OutreachShares_WallPostId_GroupId",
                table: "OutreachShares",
                columns: new[] { "WallPostId", "GroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutreachWallPosts_FacebookAccountId_CreatedAtUtc",
                table: "OutreachWallPosts",
                columns: new[] { "FacebookAccountId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutreachShares");

            migrationBuilder.DropTable(
                name: "OutreachWallPosts");
        }
    }
}
