using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGenerationOptionsCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "generation_model_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    model_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    supported_ratios = table.Column<string>(type: "jsonb", nullable: false),
                    supported_qualities = table.Column<string>(type: "jsonb", nullable: false),
                    supports_resolution = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("generation_model_options_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "generation_social_presets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content_label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    supported_ratios = table.Column<string>(type: "jsonb", nullable: false),
                    default_ratio = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("generation_social_presets_pkey", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_generation_model_options_mode_active_sort",
                table: "generation_model_options",
                columns: new[] { "mode", "is_active", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_generation_model_options_mode_model_id",
                table: "generation_model_options",
                columns: new[] { "mode", "model_id" });

            migrationBuilder.CreateIndex(
                name: "ix_generation_social_presets_identity",
                table: "generation_social_presets",
                columns: new[] { "mode", "platform", "content_type" });

            migrationBuilder.CreateIndex(
                name: "ix_generation_social_presets_mode_active_sort",
                table: "generation_social_presets",
                columns: new[] { "mode", "is_active", "sort_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "generation_model_options");

            migrationBuilder.DropTable(
                name: "generation_social_presets");
        }
    }
}
