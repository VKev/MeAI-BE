using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserScopedPublicationsAndSocialPostWorkspaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_post_publications_external_content",
                table: "post_publications");

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "post_publications",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE post_publications AS publication
                SET user_id = post.user_id
                FROM posts AS post
                WHERE publication.post_id = post.id
                  AND publication.user_id IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "post_publications",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "social_media_post_workspaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    social_media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("social_media_post_workspaces_pkey", x => x.id);
                    table.ForeignKey(
                        name: "social_media_post_workspaces_post_id_fkey",
                        column: x => x.post_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_post_publications_external_content",
                table: "post_publications",
                columns: new[] { "user_id", "social_media_type", "destination_owner_id", "external_content_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_social_media_post_workspaces_post_id",
                table: "social_media_post_workspaces",
                column: "post_id");

            migrationBuilder.CreateIndex(
                name: "ix_social_media_post_workspaces_user_workspace_post",
                table: "social_media_post_workspaces",
                columns: new[] { "user_id", "workspace_id", "post_id" });

            migrationBuilder.CreateIndex(
                name: "ux_social_media_post_workspaces_user_workspace_social_post",
                table: "social_media_post_workspaces",
                columns: new[] { "user_id", "workspace_id", "social_media_id", "post_id" },
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO social_media_post_workspaces (
                    id,
                    user_id,
                    post_id,
                    social_media_id,
                    workspace_id,
                    created_at,
                    updated_at,
                    deleted_at)
                SELECT
                    gen_random_uuid(),
                    post.user_id,
                    publication.post_id,
                    publication.social_media_id,
                    publication.workspace_id,
                    publication.created_at,
                    publication.updated_at,
                    NULL
                FROM post_publications AS publication
                INNER JOIN posts AS post ON post.id = publication.post_id
                WHERE publication.deleted_at IS NULL
                  AND publication.social_media_id <> '00000000-0000-0000-0000-000000000000'
                  AND publication.workspace_id <> '00000000-0000-0000-0000-000000000000'
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO social_media_post_workspaces (
                    id,
                    user_id,
                    post_id,
                    social_media_id,
                    workspace_id,
                    created_at,
                    updated_at,
                    deleted_at)
                SELECT
                    gen_random_uuid(),
                    post.user_id,
                    post.id,
                    post.social_media_id,
                    post.workspace_id,
                    COALESCE(post.created_at, CURRENT_TIMESTAMP),
                    post.updated_at,
                    NULL
                FROM posts AS post
                WHERE post.deleted_at IS NULL
                  AND post.social_media_id IS NOT NULL
                  AND post.workspace_id IS NOT NULL
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "social_media_post_workspaces");

            migrationBuilder.DropIndex(
                name: "ux_post_publications_external_content",
                table: "post_publications");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "post_publications");

            migrationBuilder.CreateIndex(
                name: "ux_post_publications_external_content",
                table: "post_publications",
                columns: new[] { "social_media_type", "destination_owner_id", "external_content_id" },
                unique: true);
        }
    }
}
