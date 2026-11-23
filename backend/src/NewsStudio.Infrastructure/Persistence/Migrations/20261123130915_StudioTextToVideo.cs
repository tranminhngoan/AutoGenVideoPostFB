using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StudioTextToVideo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BibleJson",
                table: "StudioVideos",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Seed",
                table: "StudioVideos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VideoModel",
                table: "StudioVideos",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "InteractionId",
                table: "StudioShots",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CastJson",
                table: "StudioShots",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "StudioShots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoiceBlobId",
                table: "StudioShots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "VoiceSeconds",
                table: "StudioShots",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BibleJson",
                table: "StudioVideos");

            migrationBuilder.DropColumn(
                name: "Seed",
                table: "StudioVideos");

            migrationBuilder.DropColumn(
                name: "VideoModel",
                table: "StudioVideos");

            migrationBuilder.DropColumn(
                name: "CastJson",
                table: "StudioShots");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "StudioShots");

            migrationBuilder.DropColumn(
                name: "VoiceBlobId",
                table: "StudioShots");

            migrationBuilder.DropColumn(
                name: "VoiceSeconds",
                table: "StudioShots");

            migrationBuilder.AlterColumn<string>(
                name: "InteractionId",
                table: "StudioShots",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
