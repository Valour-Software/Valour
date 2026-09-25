using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Valour.Database.Context;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <summary>
    /// Adds the columns a device-link session uses to relay the approving
    /// device's proof and its sealed pins to the new device.
    /// </summary>
    [DbContext(typeof(ValourDb))]
    [Migration("20260925120000_DeviceLinkApproval")]
    public partial class DeviceLinkApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "approval_mac",
                table: "e2ee_device_link_sessions",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "approval_pins",
                table: "e2ee_device_link_sessions",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approval_mac",
                table: "e2ee_device_link_sessions");

            migrationBuilder.DropColumn(
                name: "approval_pins",
                table: "e2ee_device_link_sessions");
        }
    }
}
