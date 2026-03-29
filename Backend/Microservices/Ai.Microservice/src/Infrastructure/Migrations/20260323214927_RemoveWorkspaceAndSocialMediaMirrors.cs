using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveWorkspaceAndSocialMediaMirrors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "chat_sessions_workspace_id_fkey",
                table: "chat_sessions");

            migrationBuilder.DropForeignKey(
                name: "post_publications_workspace_id_fkey",
                table: "post_publications");

            migrationBuilder.DropForeignKey(
                name: "posts_workspace_id_fkey",
                table: "posts");

            migrationBuilder.DropTable(
                name: "social_medias");

            migrationBuilder.DropTable(
                name: "workspaces");

            migrationBuilder.DropIndex(
                name: "IX_chat_sessions_workspace_id",
                table: "chat_sessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "social_medias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_token = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    refresh_token = table.Column<string>(type: "text", nullable: true),
                    social_media_type = table.Column<string>(type: "text", nullable: false),
                    token_type = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("social_medias_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workspaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_name = table.Column<string>(type: "text", nullable: false),
                    workspace_type = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("workspaces_pkey", x => x.id);
                });

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

            migrationBuilder.AddForeignKey(
                name: "post_publications_workspace_id_fkey",
                table: "post_publications",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "posts_workspace_id_fkey",
                table: "posts",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
