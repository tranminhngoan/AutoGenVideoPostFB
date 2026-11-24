using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StudioInstagram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstagramError",
                table: "StudioVideos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramPermalink",
                table: "StudioVideos",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstagramError",
                table: "StudioVideos");

            migrationBuilder.DropColumn(
                name: "InstagramPermalink",
                table: "StudioVideos");
        }
    }
}
