using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationHistoryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_notifications_payload_json_gin
                ON notifications USING gin (payload_json jsonb_path_ops)
                WHERE payload_json IS NOT NULL;
                """,
                suppressTransaction: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_user_created_at",
                table: "user_notifications",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_source_type_created_at",
                table: "notifications",
                columns: new[] { "source", "type", "created_at" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX CONCURRENTLY IF EXISTS ix_notifications_payload_json_gin;
                """,
                suppressTransaction: true);

            migrationBuilder.DropIndex(
                name: "ix_user_notifications_user_created_at",
                table: "user_notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_source_type_created_at",
                table: "notifications");
        }
    }
}
