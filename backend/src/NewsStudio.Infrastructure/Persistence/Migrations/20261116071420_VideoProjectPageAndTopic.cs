using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VideoProjectPageAndTopic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FacebookAccountId",
                table: "VideoProjects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Topic",
                table: "VideoProjects",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FacebookAccountId",
                table: "VideoProjects");

            migrationBuilder.DropColumn(
                name: "Topic",
                table: "VideoProjects");
        }
    }
}
