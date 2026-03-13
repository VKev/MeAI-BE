using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplacePostAnalyticsSnapshotsWithPostMetricSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "post_metric_snapshots");

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

            migrationBuilder.CreateIndex(
                name: "ix_post_metric_snapshots_user_social_retrieved_at",
                table: "post_metric_snapshots",
                columns: new[] { "user_id", "social_media_id", "retrieved_at" });

            migrationBuilder.CreateIndex(
                name: "ux_post_metric_snapshots_user_social_post",
                table: "post_metric_snapshots",
                columns: new[] { "user_id", "social_media_id", "platform_post_id" },
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO post_metric_snapshots (
                    id,
                    user_id,
                    social_media_id,
                    platform,
                    platform_post_id,
                    post_payload_json,
                    view_count,
                    like_count,
                    comment_count,
                    reply_count,
                    share_count,
                    repost_count,
                    raw_metrics_json,
                    quote_count,
                    retrieved_at,
                    created_at,
                    updated_at
                )
                SELECT
                    id,
                    user_id,
                    social_media_id,
                    platform,
                    platform_post_id,
                    post_payload_json,
                    CASE WHEN stats_payload_json IS NULL THEN NULL ELSE NULLIF(stats_payload_json::jsonb ->> 'views', '')::bigint END,
                    CASE WHEN stats_payload_json IS NULL THEN NULL ELSE NULLIF(stats_payload_json::jsonb ->> 'likes', '')::bigint END,
                    CASE WHEN stats_payload_json IS NULL THEN NULL ELSE NULLIF(stats_payload_json::jsonb ->> 'comments', '')::bigint END,
                    CASE WHEN stats_payload_json IS NULL THEN NULL ELSE NULLIF(stats_payload_json::jsonb ->> 'replies', '')::bigint END,
                    CASE WHEN stats_payload_json IS NULL THEN NULL ELSE NULLIF(stats_payload_json::jsonb ->> 'shares', '')::bigint END,
                    CASE WHEN stats_payload_json IS NULL THEN NULL ELSE NULLIF(stats_payload_json::jsonb ->> 'reposts', '')::bigint END,
                    stats_payload_json,
                    CASE WHEN stats_payload_json IS NULL THEN NULL ELSE NULLIF(stats_payload_json::jsonb ->> 'quotes', '')::bigint END,
                    retrieved_at,
                    created_at,
                    updated_at
                FROM post_analytics_snapshots;
                """);

            migrationBuilder.DropTable(
                name: "post_analytics_snapshots");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "post_analytics_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    post_id = table.Column<Guid>(type: "uuid", nullable: true),
                    social_media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<string>(type: "text", nullable: false),
                    platform_post_id = table.Column<string>(type: "text", nullable: false),
                    post_payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    stats_payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    analysis_payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    retrieved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("post_analytics_snapshots_pkey", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_post_analytics_snapshots_user_social_retrieved_at",
                table: "post_analytics_snapshots",
                columns: new[] { "user_id", "social_media_id", "retrieved_at" });

            migrationBuilder.CreateIndex(
                name: "ux_post_analytics_snapshots_user_social_post",
                table: "post_analytics_snapshots",
                columns: new[] { "user_id", "social_media_id", "platform_post_id" },
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO post_analytics_snapshots (
                    id,
                    user_id,
                    post_id,
                    social_media_id,
                    platform,
                    platform_post_id,
                    post_payload_json,
                    stats_payload_json,
                    analysis_payload_json,
                    retrieved_at,
                    created_at,
                    updated_at
                )
                SELECT
                    id,
                    user_id,
                    NULL,
                    social_media_id,
                    platform,
                    platform_post_id,
                    post_payload_json,
                    COALESCE(
                        raw_metrics_json,
                        jsonb_build_object(
                            'views', view_count,
                            'likes', like_count,
                            'comments', comment_count,
                            'replies', reply_count,
                            'shares', share_count,
                            'reposts', repost_count,
                            'quotes', quote_count,
                            'totalInteractions',
                            COALESCE(like_count, 0) +
                            COALESCE(comment_count, 0) +
                            COALESCE(reply_count, 0) +
                            COALESCE(share_count, 0) +
                            COALESCE(repost_count, 0) +
                            COALESCE(quote_count, 0)
                        )
                    ),
                    NULL,
                    retrieved_at,
                    created_at,
                    updated_at
                FROM post_metric_snapshots;
                """);

            migrationBuilder.DropTable(
                name: "post_metric_snapshots");

            migrationBuilder.CreateTable(
                name: "post_metric_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    captured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    comment_count = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    impression_count = table.Column<long>(type: "bigint", nullable: true),
                    like_count = table.Column<long>(type: "bigint", nullable: true),
                    metric_window = table.Column<string>(type: "text", nullable: false),
                    post_publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_metrics = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    reach_count = table.Column<long>(type: "bigint", nullable: true),
                    save_count = table.Column<long>(type: "bigint", nullable: true),
                    share_count = table.Column<long>(type: "bigint", nullable: true),
                    view_count = table.Column<long>(type: "bigint", nullable: true),
                    watch_time_seconds = table.Column<long>(type: "bigint", nullable: true)
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
        }
    }
}
