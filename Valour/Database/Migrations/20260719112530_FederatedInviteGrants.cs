using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class FederatedInviteGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "federated_invite_grants",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    node_domain = table.Column<string>(type: "text", nullable: false),
                    creator_user_id = table.Column<long>(type: "bigint", nullable: false),
                    intended_user_id = table.Column<long>(type: "bigint", nullable: false),
                    max_uses = table.Column<int>(type: "integer", nullable: false),
                    uses = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_federated_invite_grants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "federated_invite_redemptions",
                columns: table => new
                {
                    grant_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    redeemed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    passport = table.Column<string>(type: "text", nullable: true),
                    proof = table.Column<string>(type: "text", nullable: true),
                    reported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_federated_invite_redemptions", x => new { x.grant_id, x.user_id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_federated_invite_grants_intended_user_id",
                table: "federated_invite_grants",
                column: "intended_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_federated_invite_grants_node_domain",
                table: "federated_invite_grants",
                column: "node_domain");

            migrationBuilder.CreateIndex(
                name: "IX_federated_invite_grants_planet_id",
                table: "federated_invite_grants",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_federated_invite_redemptions_planet_id",
                table: "federated_invite_redemptions",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_federated_invite_redemptions_reported_at",
                table: "federated_invite_redemptions",
                column: "reported_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "federated_invite_grants");

            migrationBuilder.DropTable(
                name: "federated_invite_redemptions");
        }
    }
}
