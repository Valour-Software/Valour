using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Valour.Database.Context;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ValourDb))]
    [Migration("20260719160000_RemoveFederatedWorkerIds")]
    public partial class RemoveFederatedWorkerIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_federated_nodes_worker_id",
                table: "federated_nodes");

            migrationBuilder.DropColumn(
                name: "worker_id",
                table: "federated_nodes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "worker_id",
                table: "federated_nodes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_federated_nodes_worker_id",
                table: "federated_nodes",
                column: "worker_id",
                unique: true);
        }
    }
}
