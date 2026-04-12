using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeCurrencyDefaultToCAD : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "currency",
                table: "tenants",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "CAD",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldDefaultValue: "DZD");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "currency",
                table: "tenants",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "DZD",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldDefaultValue: "CAD");
        }
    }
}
