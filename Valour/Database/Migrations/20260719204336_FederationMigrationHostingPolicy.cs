using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class FederationMigrationHostingPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allows_public_migrations",
                table: "federated_nodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "federated_migration_hosting_approvals",
                columns: table => new
                {
                    node_domain = table.Column<string>(type: "text", nullable: false),
                    owner_id = table.Column<long>(type: "bigint", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_federated_migration_hosting_approvals", x => new { x.node_domain, x.owner_id, x.planet_id });
                    table.ForeignKey(
                        name: "FK_federated_migration_hosting_approvals_federated_nodes_node_~",
                        column: x => x.node_domain,
                        principalTable: "federated_nodes",
                        principalColumn: "domain",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_federated_migration_hosting_approvals_node_domain_owner_id",
                table: "federated_migration_hosting_approvals",
                columns: new[] { "node_domain", "owner_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "federated_migration_hosting_approvals");

            migrationBuilder.DropColumn(
                name: "allows_public_migrations",
                table: "federated_nodes");
        }
    }
}
