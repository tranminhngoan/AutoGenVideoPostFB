using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiPageAutoPostAndTopics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Topics",
                table: "TrendingArticles",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Topic",
                table: "AutoPostSettings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "tong-hop");

            migrationBuilder.AddColumn<string>(
                name: "TopicKeywords",
                table: "AutoPostSettings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SlotKey",
                table: "AutoPostRuns",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<Guid>(
                name: "FacebookAccountId",
                table: "AutoPostRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutoPostSettings_FacebookAccountId",
                table: "AutoPostSettings",
                column: "FacebookAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoPostRuns_FacebookAccountId",
                table: "AutoPostRuns",
                column: "FacebookAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AutoPostSettings_FacebookAccountId",
                table: "AutoPostSettings");

            migrationBuilder.DropIndex(
                name: "IX_AutoPostRuns_FacebookAccountId",
                table: "AutoPostRuns");

            migrationBuilder.DropColumn(
                name: "Topics",
                table: "TrendingArticles");

            migrationBuilder.DropColumn(
                name: "Topic",
                table: "AutoPostSettings");

            migrationBuilder.DropColumn(
                name: "TopicKeywords",
                table: "AutoPostSettings");

            migrationBuilder.DropColumn(
                name: "FacebookAccountId",
                table: "AutoPostRuns");

            migrationBuilder.AlterColumn<string>(
                name: "SlotKey",
                table: "AutoPostRuns",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }
    }
}
