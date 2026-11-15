using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NewsStudio.Infrastructure.Persistence;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Stores the AI editor, TTS voice and speaking rate chosen when a video project is created,
    /// so the background pipeline uses them instead of hard-coded defaults.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261115200000_AddProjectProductionSettings")]
    public partial class AddProjectProductionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiProvider",
                table: "VideoProjects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Voice",
                table: "VideoProjects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoiceSpeed",
                table: "VideoProjects",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AiProvider", table: "VideoProjects");
            migrationBuilder.DropColumn(name: "Voice", table: "VideoProjects");
            migrationBuilder.DropColumn(name: "VoiceSpeed", table: "VideoProjects");
        }
    }
}
