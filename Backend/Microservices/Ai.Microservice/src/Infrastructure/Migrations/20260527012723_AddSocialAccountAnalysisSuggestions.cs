using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialAccountAnalysisSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "social_account_analysis_suggestions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    social_media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    suggestion = table.Column<string>(type: "text", nullable: true),
                    error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    request_json = table.Column<string>(type: "jsonb", nullable: true),
                    response_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("social_account_analysis_suggestions_pkey", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_social_account_analysis_suggestions_correlation_id",
                table: "social_account_analysis_suggestions",
                column: "correlation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_social_account_analysis_suggestions_user_social_media",
                table: "social_account_analysis_suggestions",
                columns: new[] { "user_id", "social_media_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "social_account_analysis_suggestions");
        }
    }
}
