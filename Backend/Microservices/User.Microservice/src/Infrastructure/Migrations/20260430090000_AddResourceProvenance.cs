using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Infrastructure.Migrations
{
    [DbContext(typeof(MyDbContext))]
    [Migration("20260430090000_AddResourceProvenance")]
    public partial class AddResourceProvenance : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "origin_chat_id",
                table: "resources",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "origin_chat_session_id",
                table: "resources",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin_kind",
                table: "resources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin_source_url",
                table: "resources",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_resources_user_origin_session",
                table: "resources",
                columns: new[] { "user_id", "origin_kind", "origin_chat_session_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_resources_user_origin_session",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "origin_chat_id",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "origin_chat_session_id",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "origin_kind",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "origin_source_url",
                table: "resources");
        }
    }
}
