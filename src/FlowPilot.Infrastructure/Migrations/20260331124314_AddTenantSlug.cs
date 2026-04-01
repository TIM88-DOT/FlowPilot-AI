using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "slug",
                table: "tenants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // Backfill slugs for existing tenants from business_name
            migrationBuilder.Sql("""
                UPDATE tenants
                SET slug = LOWER(TRIM(BOTH '-' FROM REGEXP_REPLACE(TRIM(business_name), '[^a-zA-Z0-9]+', '-', 'g')))
                WHERE slug = '';

                -- De-duplicate: append row_number for collisions
                WITH duplicates AS (
                    SELECT id, slug, ROW_NUMBER() OVER (PARTITION BY slug ORDER BY created_at) AS rn
                    FROM tenants
                )
                UPDATE tenants t
                SET slug = d.slug || '-' || d.rn
                FROM duplicates d
                WHERE t.id = d.id AND d.rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                table: "tenants",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tenants_slug",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "slug",
                table: "tenants");
        }
    }
}
