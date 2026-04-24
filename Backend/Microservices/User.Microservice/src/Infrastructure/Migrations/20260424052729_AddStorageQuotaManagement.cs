using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageQuotaManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "original_file_name",
                table: "resources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "size_bytes",
                table: "resources",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "storage_bucket",
                table: "resources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "storage_key",
                table: "resources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "storage_namespace",
                table: "resources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "storage_provider",
                table: "resources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "storage_region",
                table: "resources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "free_storage_quota_bytes",
                table: "configs",
                type: "bigint",
                nullable: true,
                defaultValue: 104857600L);

            migrationBuilder.CreateIndex(
                name: "ix_resources_storage_namespace_key",
                table: "resources",
                columns: new[] { "storage_namespace", "storage_key" });

            migrationBuilder.CreateIndex(
                name: "ix_resources_user_deleted",
                table: "resources",
                columns: new[] { "user_id", "is_deleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_resources_storage_namespace_key",
                table: "resources");

            migrationBuilder.DropIndex(
                name: "ix_resources_user_deleted",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "original_file_name",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "size_bytes",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "storage_bucket",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "storage_key",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "storage_namespace",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "storage_provider",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "storage_region",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "free_storage_quota_bytes",
                table: "configs");
        }
    }
}
