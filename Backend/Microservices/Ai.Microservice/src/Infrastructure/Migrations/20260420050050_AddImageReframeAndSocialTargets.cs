using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImageReframeAndSocialTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "parent_correlation_id",
                table: "image_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "social_targets",
                table: "image_tasks",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_image_tasks_parent_correlation_id",
                table: "image_tasks",
                column: "parent_correlation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_image_tasks_parent_correlation_id",
                table: "image_tasks");

            migrationBuilder.DropColumn(
                name: "parent_correlation_id",
                table: "image_tasks");

            migrationBuilder.DropColumn(
                name: "social_targets",
                table: "image_tasks");
        }
    }
}
