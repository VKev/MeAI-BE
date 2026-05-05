using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendPost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "recommend_post_id",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recommend_posts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    improve_caption = table.Column<bool>(type: "boolean", nullable: false),
                    improve_image = table.Column<bool>(type: "boolean", nullable: false),
                    style = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "branded"),
                    user_instruction = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    result_caption = table.Column<string>(type: "text", nullable: true),
                    result_resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_presigned_url = table.Column<string>(type: "text", nullable: true),
                    result_references = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("recommend_posts_pkey", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_posts_recommend_post_id",
                table: "posts",
                column: "recommend_post_id",
                unique: true,
                filter: "recommend_post_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_recommend_posts_user_created_at",
                table: "recommend_posts",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_recommend_posts_correlation_id",
                table: "recommend_posts",
                column: "correlation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_recommend_posts_original_post_id",
                table: "recommend_posts",
                column: "original_post_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recommend_posts");

            migrationBuilder.DropIndex(
                name: "ux_posts_recommend_post_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "recommend_post_id",
                table: "posts");
        }
    }
}
