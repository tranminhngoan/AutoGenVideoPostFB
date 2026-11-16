using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EngagementFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EditorialPassJson",
                table: "VideoProjects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstCommentError",
                table: "FacebookPosts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstCommentId",
                table: "FacebookPosts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "DailyDigestRuns",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Daily");

            migrationBuilder.AddColumn<bool>(
                name: "AutoTune",
                table: "AutoPostSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "BreakingEnabled",
                table: "AutoPostSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "BreakingPerDay",
                table: "AutoPostSettings",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<bool>(
                name: "WeeklyDigestEnabled",
                table: "AutoPostSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBreaking",
                table: "AutoPostRuns",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EditorialPassJson",
                table: "VideoProjects");

            migrationBuilder.DropColumn(
                name: "FirstCommentError",
                table: "FacebookPosts");

            migrationBuilder.DropColumn(
                name: "FirstCommentId",
                table: "FacebookPosts");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "DailyDigestRuns");

            migrationBuilder.DropColumn(
                name: "AutoTune",
                table: "AutoPostSettings");

            migrationBuilder.DropColumn(
                name: "BreakingEnabled",
                table: "AutoPostSettings");

            migrationBuilder.DropColumn(
                name: "BreakingPerDay",
                table: "AutoPostSettings");

            migrationBuilder.DropColumn(
                name: "WeeklyDigestEnabled",
                table: "AutoPostSettings");

            migrationBuilder.DropColumn(
                name: "IsBreaking",
                table: "AutoPostRuns");
        }
    }
}
