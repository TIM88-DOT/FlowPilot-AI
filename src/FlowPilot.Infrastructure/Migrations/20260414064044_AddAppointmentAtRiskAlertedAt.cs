using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentAtRiskAlertedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "at_risk_alerted_at",
                table: "appointments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "at_risk_alerted_at",
                table: "appointments");
        }
    }
}
