using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShotContinueFromPrevious : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ContinueFromPrevious",
                table: "StudioShots",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContinueFromPrevious",
                table: "StudioShots");
        }
    }
}
