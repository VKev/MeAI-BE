using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowUnpublishingStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE post_publications DROP CONSTRAINT IF EXISTS ck_post_publications_publish_status;");
            migrationBuilder.Sql(
                "ALTER TABLE post_publications ADD CONSTRAINT ck_post_publications_publish_status " +
                "CHECK (publish_status IN ('processing', 'published', 'unpublishing', 'failed'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE post_publications DROP CONSTRAINT IF EXISTS ck_post_publications_publish_status;");
            migrationBuilder.Sql(
                "ALTER TABLE post_publications ADD CONSTRAINT ck_post_publications_publish_status " +
                "CHECK (publish_status IN ('processing', 'published', 'failed'));");
        }
    }
}
