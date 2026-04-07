using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPostBuilderAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "post_builder_id",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "post_builders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    post_type = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("post_builders_pkey", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_posts_post_builder_id",
                table: "posts",
                column: "post_builder_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_builders_user_workspace_created_at",
                table: "post_builders",
                columns: new[] { "user_id", "workspace_id", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_post_builders_workspace_id",
                table: "post_builders",
                column: "workspace_id");

            migrationBuilder.AddForeignKey(
                name: "posts_post_builder_id_fkey",
                table: "posts",
                column: "post_builder_id",
                principalTable: "post_builders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "posts_post_builder_id_fkey",
                table: "posts");

            migrationBuilder.DropTable(
                name: "post_builders");

            migrationBuilder.DropIndex(
                name: "ix_posts_post_builder_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "post_builder_id",
                table: "posts");
        }
    }
}
