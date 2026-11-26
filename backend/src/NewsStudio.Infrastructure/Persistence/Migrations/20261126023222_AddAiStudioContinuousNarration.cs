using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiStudioContinuousNarration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NarrationBlobId",
                table: "AiStudioProjects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NarrationKey",
                table: "AiStudioProjects",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NarrationSeconds",
                table: "AiStudioProjects",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NarrationBlobId",
                table: "AiStudioProjects");

            migrationBuilder.DropColumn(
                name: "NarrationKey",
                table: "AiStudioProjects");

            migrationBuilder.DropColumn(
                name: "NarrationSeconds",
                table: "AiStudioProjects");
        }
    }
}
