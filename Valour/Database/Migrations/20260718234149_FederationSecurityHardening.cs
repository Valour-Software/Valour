using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class FederationSecurityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "trusted_user_ids_json",
                table: "federated_planet_stubs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "snapshot_hash",
                table: "federated_migrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "snapshot_served_at",
                table: "federated_migrations",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "trusted_user_ids_json",
                table: "federated_planet_stubs");

            migrationBuilder.DropColumn(
                name: "snapshot_hash",
                table: "federated_migrations");

            migrationBuilder.DropColumn(
                name: "snapshot_served_at",
                table: "federated_migrations");
        }
    }
}
