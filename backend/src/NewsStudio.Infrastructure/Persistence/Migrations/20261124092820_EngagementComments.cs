using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EngagementComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstCommentText",
                table: "FacebookPosts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramFirstCommentError",
                table: "FacebookPosts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramFirstCommentId",
                table: "FacebookPosts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PostComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacebookPostId = table.Column<Guid>(type: "uuid", nullable: false),
                    Network = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Author = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Message = table.Column<string>(type: "text", nullable: false),
                    CommentedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SuggestedReply = table.Column<string>(type: "text", nullable: true),
                    SentReply = table.Column<string>(type: "text", nullable: true),
                    ReplyId = table.Column<string>(type: "text", nullable: true),
                    ReplyError = table.Column<string>(type: "text", nullable: true),
                    RepliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostComments_FacebookPosts_FacebookPostId",
                        column: x => x.FacebookPostId,
                        principalTable: "FacebookPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PostComments_ExternalId",
                table: "PostComments",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostComments_FacebookPostId",
                table: "PostComments",
                column: "FacebookPostId");

            migrationBuilder.CreateIndex(
                name: "IX_PostComments_Status_CommentedAtUtc",
                table: "PostComments",
                columns: new[] { "Status", "CommentedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PostComments");

            migrationBuilder.DropColumn(
                name: "FirstCommentText",
                table: "FacebookPosts");

            migrationBuilder.DropColumn(
                name: "InstagramFirstCommentError",
                table: "FacebookPosts");

            migrationBuilder.DropColumn(
                name: "InstagramFirstCommentId",
                table: "FacebookPosts");
        }
    }
}
