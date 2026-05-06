using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgenticScheduleFuturePublishingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_content_length",
                table: "publishing_schedules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "search_query_template",
                table: "publishing_schedules",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_content_length",
                table: "publishing_schedules");

            migrationBuilder.DropColumn(
                name: "search_query_template",
                table: "publishing_schedules");
        }
    }
}
