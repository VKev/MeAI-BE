using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedModerationWorkflowAndProfileTimelineIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "action_type",
                table: "reports",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_note",
                table: "reports",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at",
                table: "reports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by_admin_id",
                table: "reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_reports_status",
                table: "reports",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_posts_user_created_at_id",
                table: "posts",
                columns: new[] { "user_id", "created_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reports_status",
                table: "reports");

            migrationBuilder.DropIndex(
                name: "ix_posts_user_created_at_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "action_type",
                table: "reports");

            migrationBuilder.DropColumn(
                name: "resolution_note",
                table: "reports");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "reports");

            migrationBuilder.DropColumn(
                name: "reviewed_by_admin_id",
                table: "reports");
        }
    }
}
