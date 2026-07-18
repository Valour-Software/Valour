using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class FederationNodeS2SAndPlanetStubs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "purpose",
                table: "federation_keys",
                type: "text",
                nullable: false,
                defaultValue: "hub");

            migrationBuilder.CreateTable(
                name: "federated_planet_stubs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    node_domain = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    owner_id = table.Column<long>(type: "bigint", nullable: false),
                    member_count = table.Column<int>(type: "integer", nullable: false),
                    nsfw = table.Column<bool>(type: "boolean", nullable: false),
                    discoverable = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_federated_planet_stubs", x => x.id);
                    table.ForeignKey(
                        name: "FK_federated_planet_stubs_federated_nodes_node_domain",
                        column: x => x.node_domain,
                        principalTable: "federated_nodes",
                        principalColumn: "domain",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_federated_planet_stubs_discoverable",
                table: "federated_planet_stubs",
                column: "discoverable");

            migrationBuilder.CreateIndex(
                name: "IX_federated_planet_stubs_node_domain",
                table: "federated_planet_stubs",
                column: "node_domain");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "federated_planet_stubs");

            migrationBuilder.DropColumn(
                name: "purpose",
                table: "federation_keys");
        }
    }
}
