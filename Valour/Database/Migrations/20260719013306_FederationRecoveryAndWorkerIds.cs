using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class FederationRecoveryAndWorkerIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "worker_id",
                table: "federated_nodes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "source_discoverable",
                table: "federated_migrations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "source_public",
                table: "federated_migrations",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_federated_nodes_worker_id",
                table: "federated_nodes",
                column: "worker_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_federated_nodes_worker_id",
                table: "federated_nodes");

            migrationBuilder.DropColumn(
                name: "worker_id",
                table: "federated_nodes");

            migrationBuilder.DropColumn(
                name: "source_discoverable",
                table: "federated_migrations");

            migrationBuilder.DropColumn(
                name: "source_public",
                table: "federated_migrations");
        }
    }
}
