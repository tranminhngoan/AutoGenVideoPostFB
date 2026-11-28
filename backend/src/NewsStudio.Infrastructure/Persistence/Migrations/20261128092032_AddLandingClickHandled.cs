using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLandingClickHandled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HandledAtUtc",
                table: "LandingEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LandingEvents_Slug_Type_CreatedAtUtc",
                table: "LandingEvents",
                columns: new[] { "Slug", "Type", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LandingEvents_Slug_Type_CreatedAtUtc",
                table: "LandingEvents");

            migrationBuilder.DropColumn(
                name: "HandledAtUtc",
                table: "LandingEvents");
        }
    }
}
