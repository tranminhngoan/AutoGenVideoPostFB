using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGrowthInstagram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstagramError",
                table: "GrowthScheduledContents",
                type: "character varying(1500)",
                maxLength: 1500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramMediaId",
                table: "GrowthScheduledContents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramPermalink",
                table: "GrowthScheduledContents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AlsoInstagram",
                table: "GrowthCampaigns",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstagramError",
                table: "GrowthScheduledContents");

            migrationBuilder.DropColumn(
                name: "InstagramMediaId",
                table: "GrowthScheduledContents");

            migrationBuilder.DropColumn(
                name: "InstagramPermalink",
                table: "GrowthScheduledContents");

            migrationBuilder.DropColumn(
                name: "AlsoInstagram",
                table: "GrowthCampaigns");
        }
    }
}
