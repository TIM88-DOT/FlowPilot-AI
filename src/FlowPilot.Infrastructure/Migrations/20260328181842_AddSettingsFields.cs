using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSettingsFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "tenants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "tenants",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "DZD");

            migrationBuilder.AddColumn<string>(
                name: "booking_settings_json",
                table: "tenant_settings",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notification_settings_json",
                table: "tenant_settings",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_settings_json",
                table: "tenant_settings",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "booking_settings_json",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "notification_settings_json",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "review_settings_json",
                table: "tenant_settings");
        }
    }
}
