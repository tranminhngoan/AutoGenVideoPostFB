using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StudioSceneNarration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Scene",
                table: "StudioShots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoiceStyle",
                table: "StudioShots",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Scene",
                table: "StudioShots");

            migrationBuilder.DropColumn(
                name: "VoiceStyle",
                table: "StudioShots");
        }
    }
}
