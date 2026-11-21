using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropFilmPagesPageNumberUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FilmPages_PageNumber",
                table: "FilmPages");

            migrationBuilder.CreateIndex(
                name: "IX_FilmPages_PageNumber",
                table: "FilmPages",
                column: "PageNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FilmPages_PageNumber",
                table: "FilmPages");

            migrationBuilder.CreateIndex(
                name: "IX_FilmPages_PageNumber",
                table: "FilmPages",
                column: "PageNumber",
                unique: true);
        }
    }
}
