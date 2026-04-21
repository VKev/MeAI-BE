using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCoinTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "coin_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delta = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reference_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    balance_after = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coin_transactions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_coin_transactions_ref",
                table: "coin_transactions",
                columns: new[] { "reason", "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "ix_coin_transactions_user_created",
                table: "coin_transactions",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coin_transactions");
        }
    }
}
