using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiStudioNarration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Narration",
                table: "AiStudioScenes",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NarrationEdited",
                table: "AiStudioScenes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NarrationEn",
                table: "AiStudioScenes",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoiceBlobId",
                table: "AiStudioScenes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoiceKey",
                table: "AiStudioScenes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "VoiceSeconds",
                table: "AiStudioScenes",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NarratorStyle",
                table: "AiStudioProjects",
                type: "character varying(600)",
                maxLength: 600,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NarratorVoice",
                table: "AiStudioProjects",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Narration",
                table: "AiStudioScenes");

            migrationBuilder.DropColumn(
                name: "NarrationEdited",
                table: "AiStudioScenes");

            migrationBuilder.DropColumn(
                name: "NarrationEn",
                table: "AiStudioScenes");

            migrationBuilder.DropColumn(
                name: "VoiceBlobId",
                table: "AiStudioScenes");

            migrationBuilder.DropColumn(
                name: "VoiceKey",
                table: "AiStudioScenes");

            migrationBuilder.DropColumn(
                name: "VoiceSeconds",
                table: "AiStudioScenes");

            migrationBuilder.DropColumn(
                name: "NarratorStyle",
                table: "AiStudioProjects");

            migrationBuilder.DropColumn(
                name: "NarratorVoice",
                table: "AiStudioProjects");
        }
    }
}
