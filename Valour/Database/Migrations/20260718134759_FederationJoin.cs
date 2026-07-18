using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class FederationJoin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "federated_accepted_domains",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    domain = table.Column<string>(type: "text", nullable: false),
                    accepted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_federated_accepted_domains", x => new { x.user_id, x.domain });
                });

            migrationBuilder.CreateTable(
                name: "federated_memberships",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    node_domain = table.Column<string>(type: "text", nullable: false),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_federated_memberships", x => new { x.user_id, x.planet_id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_federated_memberships_node_domain",
                table: "federated_memberships",
                column: "node_domain");

            migrationBuilder.CreateIndex(
                name: "IX_federated_memberships_user_id",
                table: "federated_memberships",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "federated_accepted_domains");

            migrationBuilder.DropTable(
                name: "federated_memberships");
        }
    }
}
