using System;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    [DbContext(typeof(MyDbContext))]
    [Migration("20260312004500_AddPostAnalyticsSnapshots")]
    public partial class AddPostAnalyticsSnapshots : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
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
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "post_analytics_snapshots");
        }
    }
}
