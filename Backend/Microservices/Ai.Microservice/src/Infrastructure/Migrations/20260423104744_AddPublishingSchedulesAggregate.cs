using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishingSchedulesAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "publishing_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    mode = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    timezone = table.Column<string>(type: "text", nullable: true),
                    execute_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_private = table.Column<bool>(type: "boolean", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    platform_preference = table.Column<string>(type: "text", nullable: true),
                    agent_prompt = table.Column<string>(type: "text", nullable: true),
                    execution_context_json = table.Column<string>(type: "jsonb", nullable: true),
                    last_execution_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    next_retry_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error_code = table.Column<string>(type: "text", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("publishing_schedules_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "publishing_schedule_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_type = table.Column<string>(type: "text", nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    execution_behavior = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    last_execution_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("publishing_schedule_items_pkey", x => x.id);
                    table.ForeignKey(
                        name: "publishing_schedule_items_schedule_id_fkey",
                        column: x => x.schedule_id,
                        principalTable: "publishing_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "publishing_schedule_targets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    social_media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<string>(type: "text", nullable: true),
                    target_label = table.Column<string>(type: "text", nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("publishing_schedule_targets_pkey", x => x.id);
                    table.ForeignKey(
                        name: "publishing_schedule_targets_schedule_id_fkey",
                        column: x => x.schedule_id,
                        principalTable: "publishing_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_publishing_schedule_items_schedule_item_id",
                table: "publishing_schedule_items",
                columns: new[] { "schedule_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_publishing_schedule_items_schedule_sort_order",
                table: "publishing_schedule_items",
                columns: new[] { "schedule_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_publishing_schedule_targets_schedule_social_media",
                table: "publishing_schedule_targets",
                columns: new[] { "schedule_id", "social_media_id" });

            migrationBuilder.CreateIndex(
                name: "ix_publishing_schedules_status_execute_at_utc",
                table: "publishing_schedules",
                columns: new[] { "status", "execute_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_publishing_schedules_user_created_at",
                table: "publishing_schedules",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_publishing_schedules_workspace_id",
                table: "publishing_schedules",
                column: "workspace_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "publishing_schedule_items");

            migrationBuilder.DropTable(
                name: "publishing_schedule_targets");

            migrationBuilder.DropTable(
                name: "publishing_schedules");
        }
    }
}
