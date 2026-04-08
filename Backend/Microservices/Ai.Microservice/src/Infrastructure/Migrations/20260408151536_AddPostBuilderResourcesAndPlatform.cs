using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPostBuilderResourcesAndPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "platform",
                table: "posts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resource_ids",
                table: "post_builders",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "platform",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "resource_ids",
                table: "post_builders");
        }
    }
}
