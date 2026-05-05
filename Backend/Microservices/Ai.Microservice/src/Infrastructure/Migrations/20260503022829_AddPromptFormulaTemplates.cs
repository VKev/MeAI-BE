using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptFormulaTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "formula_generation_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    formula_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    formula_key_snapshot = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    rendered_prompt = table.Column<string>(type: "text", nullable: false),
                    variables_json = table.Column<string>(type: "text", nullable: false),
                    output_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_formula_generation_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_formula_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    template = table.Column<string>(type: "text", nullable: false),
                    output_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    default_language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    default_instruction = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_formula_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_formula_generation_logs_created_at",
                table: "formula_generation_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_formula_generation_logs_formula_created_at",
                table: "formula_generation_logs",
                columns: new[] { "formula_template_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_formula_generation_logs_user_created_at",
                table: "formula_generation_logs",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_prompt_formula_templates_key",
                table: "prompt_formula_templates",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_prompt_formula_templates_output_type_active",
                table: "prompt_formula_templates",
                columns: new[] { "output_type", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "formula_generation_logs");

            migrationBuilder.DropTable(
                name: "prompt_formula_templates");
        }
    }
}
