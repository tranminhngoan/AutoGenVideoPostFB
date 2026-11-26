using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiStudioWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FacebookAccountId",
                table: "AiStudioProjects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Watermark",
                table: "AiStudioProjects",
                type: "boolean",
                nullable: false,
                defaultValue: true); // existing projects get the anti-reupload watermark too
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FacebookAccountId",
                table: "AiStudioProjects");

            migrationBuilder.DropColumn(
                name: "Watermark",
                table: "AiStudioProjects");
        }
    }
}
