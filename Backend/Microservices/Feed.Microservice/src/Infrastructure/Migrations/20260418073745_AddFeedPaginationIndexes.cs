using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedPaginationIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_posts_created_at_id",
                table: "posts",
                columns: new[] { "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_comments_parent_created_at_id",
                table: "comments",
                columns: new[] { "parent_comment_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_comments_post_parent_created_at_id",
                table: "comments",
                columns: new[] { "post_id", "parent_comment_id", "created_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_posts_created_at_id",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_comments_parent_created_at_id",
                table: "comments");

            migrationBuilder.DropIndex(
                name: "ix_comments_post_parent_created_at_id",
                table: "comments");
        }
    }
}
