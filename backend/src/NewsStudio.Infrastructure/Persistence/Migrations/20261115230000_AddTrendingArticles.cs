using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NewsStudio.Infrastructure.Persistence;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <summary>Stores RSS news items collected for the "Tin đang hot" list.</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261115230000_AddTrendingArticles")]
    public partial class AddTrendingArticles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrendingArticles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SourceKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Region = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                    ClusterId = table.Column<Guid>(type: "uuid", nullable: true),
                    CoverageCount = table.Column<int>(type: "integer", nullable: false),
                    RelatedSources = table.Column<string>(type: "text", nullable: true),
                    TrendKeyword = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TrendTraffic = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrendingArticles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrendingArticles_Url",
                table: "TrendingArticles",
                column: "Url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrendingArticles_PublishedAtUtc",
                table: "TrendingArticles",
                column: "PublishedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TrendingArticles_ClusterId",
                table: "TrendingArticles",
                column: "ClusterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TrendingArticles");
        }
    }
}
