using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPostSchedulingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ai_post_id",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "schedule_group_id",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "schedule_timezone",
                table: "posts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "scheduled_at_utc",
                table: "posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "scheduled_is_private",
                table: "posts",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "scheduled_social_media_ids",
                table: "posts",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_post_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "schedule_group_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "schedule_timezone",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "scheduled_at_utc",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "scheduled_is_private",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "scheduled_social_media_ids",
                table: "posts");
        }
    }
}
