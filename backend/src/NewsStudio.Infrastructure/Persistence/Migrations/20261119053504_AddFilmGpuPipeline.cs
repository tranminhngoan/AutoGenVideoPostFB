using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFilmGpuPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MotionPrompt",
                table: "FilmShots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotionVideoPath",
                table: "FilmShots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseMotion",
                table: "FilmShots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GpuBatchRef",
                table: "FilmEpisodes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GpuSubmittedAtUtc",
                table: "FilmEpisodes",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MotionPrompt",
                table: "FilmShots");

            migrationBuilder.DropColumn(
                name: "MotionVideoPath",
                table: "FilmShots");

            migrationBuilder.DropColumn(
                name: "UseMotion",
                table: "FilmShots");

            migrationBuilder.DropColumn(
                name: "GpuBatchRef",
                table: "FilmEpisodes");

            migrationBuilder.DropColumn(
                name: "GpuSubmittedAtUtc",
                table: "FilmEpisodes");
        }
    }
}
