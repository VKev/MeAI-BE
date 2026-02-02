using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImageTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "image_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kie_task_id = table.Column<string>(type: "text", nullable: true),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    aspect_ratio = table.Column<string>(type: "text", nullable: false),
                    resolution = table.Column<string>(type: "text", nullable: false),
                    output_format = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "ix_image_tasks_correlation_id",
                table: "image_tasks",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_image_tasks_user_id",
                table: "image_tasks",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "image_tasks");
        }
    }
}
