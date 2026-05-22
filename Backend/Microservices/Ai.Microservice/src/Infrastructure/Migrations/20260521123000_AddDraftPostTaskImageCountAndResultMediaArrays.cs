using Infrastructure.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MyDbContext))]
    [Migration("20260521123000_AddDraftPostTaskImageCountAndResultMediaArrays")]
    public partial class AddDraftPostTaskImageCountAndResultMediaArrays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "image_count",
                table: "draft_post_tasks",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "result_presigned_urls",
                table: "draft_post_tasks",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "result_resource_ids",
                table: "draft_post_tasks",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image_count",
                table: "draft_post_tasks");

            migrationBuilder.DropColumn(
                name: "result_presigned_urls",
                table: "draft_post_tasks");

            migrationBuilder.DropColumn(
                name: "result_resource_ids",
                table: "draft_post_tasks");
        }
    }
}
