using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Instagram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstagramError",
                table: "FacebookPosts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramMediaId",
                table: "FacebookPosts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramPermalink",
                table: "FacebookPosts",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramAccountId",
                table: "FacebookAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramUsername",
                table: "FacebookAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InstagramEnabled",
                table: "AutoPostSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true); // existing pages also post Reels to their linked Instagram
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstagramError",
                table: "FacebookPosts");

            migrationBuilder.DropColumn(
                name: "InstagramMediaId",
                table: "FacebookPosts");

            migrationBuilder.DropColumn(
                name: "InstagramPermalink",
                table: "FacebookPosts");

            migrationBuilder.DropColumn(
                name: "InstagramAccountId",
                table: "FacebookAccounts");

            migrationBuilder.DropColumn(
                name: "InstagramUsername",
                table: "FacebookAccounts");

            migrationBuilder.DropColumn(
                name: "InstagramEnabled",
                table: "AutoPostSettings");
        }
    }
}
