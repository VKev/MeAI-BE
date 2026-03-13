using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPostAnalyticsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "posts",
                type: "uuid",
                nullable: true);

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
                    table.CheckConstraint("ck_post_publications_publish_status", "publish_status IN ('processing', 'published', 'failed')");
                    table.ForeignKey(
                        name: "post_publications_post_id_fkey",
                        column: x => x.post_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "post_publications_workspace_id_fkey",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "post_metric_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    post_publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    captured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    metric_window = table.Column<string>(type: "text", nullable: false),
                    view_count = table.Column<long>(type: "bigint", nullable: true),
                    like_count = table.Column<long>(type: "bigint", nullable: true),
                    comment_count = table.Column<long>(type: "bigint", nullable: true),
                    share_count = table.Column<long>(type: "bigint", nullable: true),
                    save_count = table.Column<long>(type: "bigint", nullable: true),
                    impression_count = table.Column<long>(type: "bigint", nullable: true),
                    reach_count = table.Column<long>(type: "bigint", nullable: true),
                    watch_time_seconds = table.Column<long>(type: "bigint", nullable: true),
                    raw_metrics = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("post_metric_snapshots_pkey", x => x.id);
                    table.CheckConstraint("ck_post_metric_snapshots_comment_count_nonnegative", "comment_count IS NULL OR comment_count >= 0");
                    table.CheckConstraint("ck_post_metric_snapshots_impression_count_nonnegative", "impression_count IS NULL OR impression_count >= 0");
                    table.CheckConstraint("ck_post_metric_snapshots_like_count_nonnegative", "like_count IS NULL OR like_count >= 0");
                    table.CheckConstraint("ck_post_metric_snapshots_metric_window", "metric_window IN ('hour', 'day', 'lifetime')");
                    table.CheckConstraint("ck_post_metric_snapshots_reach_count_nonnegative", "reach_count IS NULL OR reach_count >= 0");
                    table.CheckConstraint("ck_post_metric_snapshots_save_count_nonnegative", "save_count IS NULL OR save_count >= 0");
                    table.CheckConstraint("ck_post_metric_snapshots_share_count_nonnegative", "share_count IS NULL OR share_count >= 0");
                    table.CheckConstraint("ck_post_metric_snapshots_view_count_nonnegative", "view_count IS NULL OR view_count >= 0");
                    table.CheckConstraint("ck_post_metric_snapshots_watch_time_seconds_nonnegative", "watch_time_seconds IS NULL OR watch_time_seconds >= 0");
                    table.ForeignKey(
                        name: "post_metric_snapshots_post_publication_id_fkey",
                        column: x => x.post_publication_id,
                        principalTable: "post_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "ix_post_metric_snapshots_captured_at",
                table: "post_metric_snapshots",
                column: "captured_at");

            migrationBuilder.CreateIndex(
                name: "ix_post_metric_snapshots_publication_captured_at",
                table: "post_metric_snapshots",
                columns: new[] { "post_publication_id", "captured_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_post_metric_snapshots_publication_captured_window",
                table: "post_metric_snapshots",
                columns: new[] { "post_publication_id", "captured_at", "metric_window" },
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

            migrationBuilder.AddForeignKey(
                name: "posts_workspace_id_fkey",
                table: "posts",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "posts_workspace_id_fkey",
                table: "posts");

            migrationBuilder.DropTable(
                name: "post_metric_snapshots");

            migrationBuilder.DropTable(
                name: "post_publications");

            migrationBuilder.DropIndex(
                name: "ix_posts_user_workspace_created_at",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_posts_workspace_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "posts");
        }
    }
}
