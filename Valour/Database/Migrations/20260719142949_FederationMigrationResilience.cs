using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations;

/// <summary>
/// Adds the durable state required to safely retry a federation migration.
/// </summary>
public partial class FederationMigrationResilience : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "grant_id",
            table: "federated_migrations",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "federated_import_receipts",
            columns: table => new
            {
                planet_id = table.Column<long>(type: "bigint", nullable: false),
                source_domain = table.Column<string>(type: "text", nullable: false),
                owner_id = table.Column<long>(type: "bigint", nullable: false),
                snapshot_hash = table.Column<string>(type: "text", nullable: false),
                source_public = table.Column<bool>(type: "boolean", nullable: false),
                source_discoverable = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_federated_import_receipts", x => x.planet_id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_federated_import_receipts_source_domain",
            table: "federated_import_receipts",
            column: "source_domain");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "federated_import_receipts");

        migrationBuilder.DropColumn(
            name: "grant_id",
            table: "federated_migrations");
    }
}
