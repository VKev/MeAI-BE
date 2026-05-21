using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendPostResultMediaArrays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "result_presigned_urls",
                table: "recommend_posts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "result_resource_ids",
                table: "recommend_posts",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "result_presigned_urls",
                table: "recommend_posts");

            migrationBuilder.DropColumn(
                name: "result_resource_ids",
                table: "recommend_posts");
        }
    }
}
