using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai",
                columns: table => new
                {
                    aiid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fullname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    phonenumber = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("ai_pkey", x => x.aiid);
                });

            migrationBuilder.CreateTable(
                name: "airole",
                columns: table => new
                {
                    roleid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    rolename = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("airole_pkey", x => x.roleid);
                });

            migrationBuilder.CreateTable(
                name: "airolemapping",
                columns: table => new
                {
                    aiid = table.Column<int>(type: "integer", nullable: false),
                    roleid = table.Column<int>(type: "integer", nullable: false),
                    assignedat = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("airolemapping_pkey", x => new { x.aiid, x.roleid });
                    table.ForeignKey(
                        name: "airolemapping_aiid_fkey",
                        column: x => x.aiid,
                        principalTable: "ai",
                        principalColumn: "aiid",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "airolemapping_roleid_fkey",
                        column: x => x.roleid,
                        principalTable: "airole",
                        principalColumn: "roleid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ai_email_key",
                table: "ai",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "airole_rolename_key",
                table: "airole",
                column: "rolename",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_airolemapping_roleid",
                table: "airolemapping",
                column: "roleid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "airolemapping");

            migrationBuilder.DropTable(
                name: "ai");

            migrationBuilder.DropTable(
                name: "airole");
        }
    }
}
