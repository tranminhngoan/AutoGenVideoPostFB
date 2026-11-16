using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DailyDigest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DigestEnabled",
                table: "AutoPostSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true); // existing pages get the evening digest

            migrationBuilder.AddColumn<int>(
                name: "DigestItems",
                table: "AutoPostSettings",
                type: "integer",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.AddColumn<string>(
                name: "DigestTime",
                table: "AutoPostSettings",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "20:30");

            migrationBuilder.CreateTable(
                name: "DailyDigestRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacebookAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    DigestDate = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IsPreview = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ItemsJson = table.Column<string>(type: "text", nullable: true),
                    Caption = table.Column<string>(type: "text", nullable: true),
                    WorkFolder = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PostId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PostError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    StoryPostId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StoryError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyDigestRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyDigestRuns_FacebookAccountId_DigestDate",
                table: "DailyDigestRuns",
                columns: new[] { "FacebookAccountId", "DigestDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyDigestRuns");

            migrationBuilder.DropColumn(
                name: "DigestEnabled",
                table: "AutoPostSettings");

            migrationBuilder.DropColumn(
                name: "DigestItems",
                table: "AutoPostSettings");

            migrationBuilder.DropColumn(
                name: "DigestTime",
                table: "AutoPostSettings");
        }
    }
}
