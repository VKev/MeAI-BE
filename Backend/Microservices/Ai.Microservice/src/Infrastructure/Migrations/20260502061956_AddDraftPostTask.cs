using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftPostTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only the draft_post_tasks table + indexes. EF auto-generated extra ops for
            // api_credentials, publishing_schedules, and the posts schedule columns because
            // those deltas were never captured in the model snapshot — but they already
            // exist in the database from earlier migrations. Re-issuing them here would
            // crash with "relation already exists" on first compose start.
            migrationBuilder.CreateTable(
                name: "draft_post_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    social_media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_prompt = table.Column<string>(type: "text", nullable: false),
                    top_k = table.Column<int>(type: "integer", nullable: false),
                    max_reference_images = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    result_post_builder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_post_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_presigned_url = table.Column<string>(type: "text", nullable: true),
                    result_caption = table.Column<string>(type: "text", nullable: true),
                    result_references = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("draft_post_tasks_pkey", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_draft_post_tasks_user_created_at",
                table: "draft_post_tasks",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_draft_post_tasks_correlation_id",
                table: "draft_post_tasks",
                column: "correlation_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "draft_post_tasks");
        }
    }
}
