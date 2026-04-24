using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPostChatSessionLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "chat_session_id",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_posts_chat_session_created_at_id",
                table: "posts",
                columns: new[] { "chat_session_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.AddForeignKey(
                name: "posts_chat_session_id_fkey",
                table: "posts",
                column: "chat_session_id",
                principalTable: "chat_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "posts_chat_session_id_fkey",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_posts_chat_session_created_at_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "chat_session_id",
                table: "posts");
        }
    }
}
