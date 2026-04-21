using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceWorkspaceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_resources_user_id",
                table: "resources");

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "resources",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_resources_user_workspace_created_at",
                table: "resources",
                columns: new[] { "user_id", "workspace_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_resources_user_workspace_created_at",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "resources");

            migrationBuilder.CreateIndex(
                name: "IX_resources_user_id",
                table: "resources",
                column: "user_id");
        }
    }
}
