using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    key_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    value_encrypted = table.Column<string>(type: "text", nullable: false),
                    value_last4 = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    last_synced_from_env_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_rotated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("api_credentials_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_name = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("chat_sessions_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "coin_pricing_catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    variant = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    unit_cost_coins = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coin_pricing_catalog", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "image_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kie_task_id = table.Column<string>(type: "text", nullable: true),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    aspect_ratio = table.Column<string>(type: "text", nullable: false),
                    resolution = table.Column<string>(type: "text", nullable: false),
                    output_format = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    parent_correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    social_targets = table.Column<string>(type: "jsonb", nullable: true),
                    result_urls = table.Column<string>(type: "jsonb", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    error_code = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("image_tasks_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "post_builders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    post_type = table.Column<string>(type: "text", nullable: true),
                    resource_ids = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("post_builders_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "post_metric_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    social_media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<string>(type: "text", nullable: false),
                    platform_post_id = table.Column<string>(type: "text", nullable: false),
                    post_payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    view_count = table.Column<long>(type: "bigint", nullable: true),
                    like_count = table.Column<long>(type: "bigint", nullable: true),
                    comment_count = table.Column<long>(type: "bigint", nullable: true),
                    reply_count = table.Column<long>(type: "bigint", nullable: true),
                    share_count = table.Column<long>(type: "bigint", nullable: true),
                    repost_count = table.Column<long>(type: "bigint", nullable: true),
                    raw_metrics_json = table.Column<string>(type: "jsonb", nullable: true),
                    quote_count = table.Column<long>(type: "bigint", nullable: true),
                    retrieved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("post_metric_snapshots_pkey", x => x.id);
                    table.CheckConstraint("ck_post_metric_snapshots_comment_count_nonnegative", "comment_count IS NULL OR comment_count >= 0");
                    table.CheckConstraint("ck_post_metric_snapshots_like_count_nonnegative", "like_count IS NULL OR like_count >= 0");
                    table.CheckConstraint("ck_post_metric_snapshots_quote_count_nonnegative", "quote_count IS NULL OR quote_count >= 0");
                    table.CheckConstraint("ck_post_metric_snapshots_reply_count_nonnegative", "reply_count IS NULL OR reply_count >= 0");
                    table.CheckConstraint("ck_post_metric_snapshots_repost_count_nonnegative", "repost_count IS NULL OR repost_count >= 0");
                    table.CheckConstraint("ck_post_metric_snapshots_share_count_nonnegative", "share_count IS NULL OR share_count >= 0");
                    table.CheckConstraint("ck_post_metric_snapshots_view_count_nonnegative", "view_count IS NULL OR view_count >= 0");
                });

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
                name: "video_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    veo_task_id = table.Column<string>(type: "text", nullable: true),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    aspect_ratio = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    result_urls = table.Column<string>(type: "jsonb", nullable: true),
                    resolution = table.Column<string>(type: "text", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    error_code = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("video_tasks_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: true),
                    config = table.Column<string>(type: "json", nullable: true),
                    reference_resource_ids = table.Column<string>(type: "json", nullable: true),
                    result_resource_ids = table.Column<string>(type: "json", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("chats_pkey", x => x.id);
                    table.ForeignKey(
                        name: "chats_session_id_fkey",
                        column: x => x.session_id,
                        principalTable: "chat_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "posts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    post_builder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    chat_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    social_media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    platform = table.Column<string>(type: "text", nullable: true),
                    title = table.Column<string>(type: "text", nullable: true),
                    content = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    schedule_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scheduled_social_media_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    scheduled_is_private = table.Column<bool>(type: "boolean", nullable: true),
                    schedule_timezone = table.Column<string>(type: "text", nullable: true),
                    scheduled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("posts_pkey", x => x.id);
                    table.ForeignKey(
                        name: "posts_chat_session_id_fkey",
                        column: x => x.chat_session_id,
                        principalTable: "chat_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "posts_post_builder_id_fkey",
                        column: x => x.post_builder_id,
                        principalTable: "post_builders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateTable(
                name: "post_publications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    social_media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    social_media_type = table.Column<string>(type: "text", nullable: false),
                    destination_owner_id = table.Column<string>(type: "text", nullable: false),
                    external_content_id = table.Column<string>(type: "text", nullable: false),
                    external_content_id_type = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    publish_status = table.Column<string>(type: "text", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_metrics_sync_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("post_publications_pkey", x => x.id);
                    table.CheckConstraint("ck_post_publications_external_content_id_type", "external_content_id_type IN ('post_id', 'publish_id')");
                    table.CheckConstraint("ck_post_publications_publish_status", "publish_status IN ('processing', 'published', 'unpublishing', 'failed')");
                    table.ForeignKey(
                        name: "post_publications_post_id_fkey",
                        column: x => x.post_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "post_resources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("post_resources_pkey", x => x.id);
                    table.ForeignKey(
                        name: "post_resources_post_id_fkey",
                        column: x => x.post_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_credentials_service_provider_key",
                table: "api_credentials",
                columns: new[] { "service_name", "provider", "key_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "chat_sessions_user_id_workspace_id_created_at_id_idx",
                table: "chat_sessions",
                columns: new[] { "user_id", "workspace_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_chats_session_id",
                table: "chats",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_coin_pricing_catalog_lookup",
                table: "coin_pricing_catalog",
                columns: new[] { "action_type", "model", "variant", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_image_tasks_correlation_id",
                table: "image_tasks",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_image_tasks_parent_correlation_id",
                table: "image_tasks",
                column: "parent_correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_image_tasks_user_id",
                table: "image_tasks",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_builders_user_workspace_created_at",
                table: "post_builders",
                columns: new[] { "user_id", "workspace_id", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_post_builders_workspace_id",
                table: "post_builders",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_metric_snapshots_user_social_retrieved_at",
                table: "post_metric_snapshots",
                columns: new[] { "user_id", "social_media_id", "retrieved_at" });

            migrationBuilder.CreateIndex(
                name: "ux_post_metric_snapshots_user_social_post",
                table: "post_metric_snapshots",
                columns: new[] { "user_id", "social_media_id", "platform_post_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_post_publications_post_created_at",
                table: "post_publications",
                columns: new[] { "post_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_post_publications_publish_status_last_metrics_sync_at",
                table: "post_publications",
                columns: new[] { "publish_status", "last_metrics_sync_at" });

            migrationBuilder.CreateIndex(
                name: "ix_post_publications_workspace_published_at",
                table: "post_publications",
                columns: new[] { "workspace_id", "published_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_post_publications_external_content",
                table: "post_publications",
                columns: new[] { "social_media_type", "destination_owner_id", "external_content_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_post_resources_post_id",
                table: "post_resources",
                column: "post_id");

            migrationBuilder.CreateIndex(
                name: "ix_posts_chat_session_created_at_id",
                table: "posts",
                columns: new[] { "chat_session_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_posts_post_builder_id",
                table: "posts",
                column: "post_builder_id");

            migrationBuilder.CreateIndex(
                name: "ix_posts_status_scheduled_at_utc",
                table: "posts",
                columns: new[] { "status", "scheduled_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_posts_user_workspace_created_at",
                table: "posts",
                columns: new[] { "user_id", "workspace_id", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_posts_workspace_id",
                table: "posts",
                column: "workspace_id");

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

            migrationBuilder.CreateIndex(
                name: "ix_video_tasks_correlation_id",
                table: "video_tasks",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_video_tasks_user_id",
                table: "video_tasks",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_credentials");

            migrationBuilder.DropTable(
                name: "chats");

            migrationBuilder.DropTable(
                name: "coin_pricing_catalog");

            migrationBuilder.DropTable(
                name: "image_tasks");

            migrationBuilder.DropTable(
                name: "post_metric_snapshots");

            migrationBuilder.DropTable(
                name: "post_publications");

            migrationBuilder.DropTable(
                name: "post_resources");

            migrationBuilder.DropTable(
                name: "publishing_schedule_items");

            migrationBuilder.DropTable(
                name: "publishing_schedule_targets");

            migrationBuilder.DropTable(
                name: "video_tasks");

            migrationBuilder.DropTable(
                name: "posts");

            migrationBuilder.DropTable(
                name: "publishing_schedules");

            migrationBuilder.DropTable(
                name: "chat_sessions");

            migrationBuilder.DropTable(
                name: "post_builders");
        }
    }
}
