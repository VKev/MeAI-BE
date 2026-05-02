using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPostBuilderOriginKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "origin_kind",
                table: "post_builders",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE post_builders
                SET origin_kind = 'ai_gemini_draft'
                WHERE origin_kind IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "origin_kind",
                table: "post_builders");
        }
    }
}
