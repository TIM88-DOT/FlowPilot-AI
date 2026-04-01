using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateCustomerUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_customers_phone_tenant_id",
                table: "customers");

            migrationBuilder.CreateIndex(
                name: "ix_customers_phone_first_name_tenant_id",
                table: "customers",
                columns: new[] { "phone", "first_name", "tenant_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_customers_phone_first_name_tenant_id",
                table: "customers");

            migrationBuilder.CreateIndex(
                name: "ix_customers_phone_tenant_id",
                table: "customers",
                columns: new[] { "phone", "tenant_id" },
                unique: true);
        }
    }
}
