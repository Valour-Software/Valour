using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class FederationPurgeDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "node_domain",
                table: "federated_purges",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_federated_purges_node_domain_id",
                table: "federated_purges",
                columns: new[] { "node_domain", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_federated_purges_node_domain_id",
                table: "federated_purges");

            migrationBuilder.DropColumn(
                name: "node_domain",
                table: "federated_purges");
        }
    }
}
