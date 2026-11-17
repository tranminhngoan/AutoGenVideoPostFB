using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NewsroomUpgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FactSheetJson",
                table: "VideoScripts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GateJson",
                table: "VideoProjects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GateStatus",
                table: "VideoProjects",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "PENDING");

            migrationBuilder.AddColumn<byte[]>(
                name: "BrandLogo",
                table: "AutoPostSettings",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WritingTone",
                table: "AutoPostSettings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "neutral");

            migrationBuilder.AddColumn<double>(
                name: "Confidence",
                table: "ArticleImages",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageType",
                table: "ArticleImages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseEvidence",
                table: "ArticleImages",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseType",
                table: "ArticleImages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceName",
                table: "ArticleImages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourcePageUrl",
                table: "ArticleImages",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectName",
                table: "ArticleImages",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectType",
                table: "ArticleImages",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationStatus",
                table: "ArticleImages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FreeKey = table.Column<bool>(type: "boolean", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    VideoProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsages_CreatedAtUtc",
                table: "AiUsages",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiUsages");

            migrationBuilder.DropColumn(
                name: "FactSheetJson",
                table: "VideoScripts");

            migrationBuilder.DropColumn(
                name: "GateJson",
                table: "VideoProjects");

            migrationBuilder.DropColumn(
                name: "GateStatus",
                table: "VideoProjects");

            migrationBuilder.DropColumn(
                name: "BrandLogo",
                table: "AutoPostSettings");

            migrationBuilder.DropColumn(
                name: "WritingTone",
                table: "AutoPostSettings");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "ArticleImages");

            migrationBuilder.DropColumn(
                name: "ImageType",
                table: "ArticleImages");

            migrationBuilder.DropColumn(
                name: "LicenseEvidence",
                table: "ArticleImages");

            migrationBuilder.DropColumn(
                name: "LicenseType",
                table: "ArticleImages");

            migrationBuilder.DropColumn(
                name: "SourceName",
                table: "ArticleImages");

            migrationBuilder.DropColumn(
                name: "SourcePageUrl",
                table: "ArticleImages");

            migrationBuilder.DropColumn(
                name: "SubjectName",
                table: "ArticleImages");

            migrationBuilder.DropColumn(
                name: "SubjectType",
                table: "ArticleImages");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "ArticleImages");
        }
    }
}
