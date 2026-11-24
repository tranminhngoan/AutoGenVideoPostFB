using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PostTrackingAndFollowers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "VideoProjectId",
                table: "FacebookPosts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "FacebookPosts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Tool");

            migrationBuilder.AddColumn<Guid>(
                name: "StudioVideoId",
                table: "FacebookPosts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "FacebookPosts",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PageFollowerSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacebookAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    FacebookFollowers = table.Column<long>(type: "bigint", nullable: false),
                    InstagramFollowers = table.Column<long>(type: "bigint", nullable: true),
                    CollectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageFollowerSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FacebookPosts_InstagramMediaId",
                table: "FacebookPosts",
                column: "InstagramMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_FacebookPosts_StudioVideoId",
                table: "FacebookPosts",
                column: "StudioVideoId");

            migrationBuilder.CreateIndex(
                name: "IX_PageFollowerSnapshots_FacebookAccountId_Date",
                table: "PageFollowerSnapshots",
                columns: new[] { "FacebookAccountId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PageFollowerSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_FacebookPosts_InstagramMediaId",
                table: "FacebookPosts");

            migrationBuilder.DropIndex(
                name: "IX_FacebookPosts_StudioVideoId",
                table: "FacebookPosts");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "FacebookPosts");

            migrationBuilder.DropColumn(
                name: "StudioVideoId",
                table: "FacebookPosts");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "FacebookPosts");

            migrationBuilder.AlterColumn<Guid>(
                name: "VideoProjectId",
                table: "FacebookPosts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
