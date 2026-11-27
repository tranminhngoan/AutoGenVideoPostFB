using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGrowthNewsMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentMode",
                table: "GrowthAutopilots",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Auto");

            migrationBuilder.CreateTable(
                name: "GrowthNewsUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacebookAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                    TrendingArticleId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClusterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    VideoProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrowthNewsUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrowthNewsUsages_FacebookAccounts_FacebookAccountId",
                        column: x => x.FacebookAccountId,
                        principalTable: "FacebookAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GrowthNewsUsages_CreatedAtUtc",
                table: "GrowthNewsUsages",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_GrowthNewsUsages_FacebookAccountId_Url",
                table: "GrowthNewsUsages",
                columns: new[] { "FacebookAccountId", "Url" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GrowthNewsUsages");

            migrationBuilder.DropColumn(
                name: "ContentMode",
                table: "GrowthAutopilots");
        }
    }
}
