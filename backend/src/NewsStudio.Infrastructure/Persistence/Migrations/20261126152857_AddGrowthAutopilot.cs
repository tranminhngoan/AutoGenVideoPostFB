using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGrowthAutopilot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ClipSeconds",
                table: "GrowthScheduledContents",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ClipStartSeconds",
                table: "GrowthScheduledContents",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ScriptId",
                table: "GrowthScheduledContents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GrowthAutopilots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacebookAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ScriptPost = table.Column<bool>(type: "boolean", nullable: false),
                    DailyVideo = table.Column<bool>(type: "boolean", nullable: false),
                    StoriesPerDay = table.Column<int>(type: "integer", nullable: false),
                    LastRunAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NoticesJson = table.Column<string>(type: "text", nullable: false),
                    StateJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrowthAutopilots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrowthAutopilots_FacebookAccounts_FacebookAccountId",
                        column: x => x.FacebookAccountId,
                        principalTable: "FacebookAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GrowthScripts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    CategoryName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Mood = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Era = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Region = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Synopsis = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Hook = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    EndingNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ScenesJson = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrowthScripts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GrowthScriptUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacebookAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScriptId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrowthScriptUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrowthScriptUsages_FacebookAccounts_FacebookAccountId",
                        column: x => x.FacebookAccountId,
                        principalTable: "FacebookAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GrowthScriptUsages_GrowthScripts_ScriptId",
                        column: x => x.ScriptId,
                        principalTable: "GrowthScripts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GrowthScheduledContents_ScriptId",
                table: "GrowthScheduledContents",
                column: "ScriptId");

            migrationBuilder.CreateIndex(
                name: "IX_GrowthAutopilots_FacebookAccountId",
                table: "GrowthAutopilots",
                column: "FacebookAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GrowthScripts_Code",
                table: "GrowthScripts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GrowthScriptUsages_FacebookAccountId_ScriptId",
                table: "GrowthScriptUsages",
                columns: new[] { "FacebookAccountId", "ScriptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GrowthScriptUsages_ScriptId",
                table: "GrowthScriptUsages",
                column: "ScriptId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GrowthAutopilots");

            migrationBuilder.DropTable(
                name: "GrowthScriptUsages");

            migrationBuilder.DropTable(
                name: "GrowthScripts");

            migrationBuilder.DropIndex(
                name: "IX_GrowthScheduledContents_ScriptId",
                table: "GrowthScheduledContents");

            migrationBuilder.DropColumn(
                name: "ClipSeconds",
                table: "GrowthScheduledContents");

            migrationBuilder.DropColumn(
                name: "ClipStartSeconds",
                table: "GrowthScheduledContents");

            migrationBuilder.DropColumn(
                name: "ScriptId",
                table: "GrowthScheduledContents");
        }
    }
}
