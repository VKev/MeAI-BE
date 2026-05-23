using Infrastructure.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MyDbContext))]
    [Migration("20260520065000_RemoveDataUrlResourceOriginSources")]
    public partial class RemoveDataUrlResourceOriginSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE resources
                SET origin_source_url = NULL,
                    updated_at = NOW()
                WHERE origin_source_url ILIKE 'data:%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
