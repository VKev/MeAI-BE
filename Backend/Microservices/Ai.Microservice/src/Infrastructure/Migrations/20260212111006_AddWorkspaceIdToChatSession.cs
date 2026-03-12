using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceIdToChatSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "chat_sessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "chat_sessions_user_id_workspace_id_created_at_id_idx",
                table: "chat_sessions",
                columns: new[] { "user_id", "workspace_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_sessions_workspace_id",
                table: "chat_sessions",
                column: "workspace_id");

            migrationBuilder.AddForeignKey(
                name: "chat_sessions_workspace_id_fkey",
                table: "chat_sessions",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "chat_sessions_workspace_id_fkey",
                table: "chat_sessions");

            migrationBuilder.DropIndex(
                name: "chat_sessions_user_id_workspace_id_created_at_id_idx",
                table: "chat_sessions");

            migrationBuilder.DropIndex(
                name: "IX_chat_sessions_workspace_id",
                table: "chat_sessions");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "chat_sessions");
        }
    }
}
