using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiSpendRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_spend_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    variant = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    unit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_cost_coins = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total_coins = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    reference_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reference_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("ai_spend_records_pkey", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_spend_records_action_model_created_at",
                table: "ai_spend_records",
                columns: new[] { "action_type", "model", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_spend_records_created_at",
                table: "ai_spend_records",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_ai_spend_records_reference",
                table: "ai_spend_records",
                columns: new[] { "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_spend_records_user_created_at",
                table: "ai_spend_records",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_spend_records");
        }
    }
}
