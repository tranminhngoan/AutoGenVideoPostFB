using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiStudioLogicReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewedAction",
                table: "AiStudioScenes",
                type: "character varying(900)",
                maxLength: 900,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedRules",
                table: "AiStudioScenes",
                type: "character varying(900)",
                maxLength: 900,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewedAction",
                table: "AiStudioScenes");

            migrationBuilder.DropColumn(
                name: "ReviewedRules",
                table: "AiStudioScenes");
        }
    }
}
