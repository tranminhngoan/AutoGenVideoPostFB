using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StudioSoundEffects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SfxEnabled",
                table: "StudioVideos",
                type: "boolean",
                nullable: false,
                defaultValue: true); // existing videos get sound effects too

            migrationBuilder.AddColumn<double>(
                name: "SfxVolume",
                table: "StudioVideos",
                type: "double precision",
                nullable: false,
                defaultValue: 0.7);

            migrationBuilder.AddColumn<Guid>(
                name: "SfxBlobId",
                table: "StudioShots",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SfxEnabled",
                table: "StudioVideos");

            migrationBuilder.DropColumn(
                name: "SfxVolume",
                table: "StudioVideos");

            migrationBuilder.DropColumn(
                name: "SfxBlobId",
                table: "StudioShots");
        }
    }
}
