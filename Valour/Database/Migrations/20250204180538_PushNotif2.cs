using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class PushNotif2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notification_subscriptions_planet_members_member_id",
                table: "notification_subscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_notification_subscriptions_planets_planet_id",
                table: "notification_subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_notification_subscriptions_member_id",
                table: "notification_subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_notification_subscriptions_planet_id",
                table: "notification_subscriptions");

            migrationBuilder.DropColumn(
                name: "member_id",
                table: "notification_subscriptions");

            migrationBuilder.DropColumn(
                name: "planet_id",
                table: "notification_subscriptions");

            migrationBuilder.RenameColumn(
                name: "role_hash_key",
                table: "notification_subscriptions",
                newName: "PlanetMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_subscriptions_PlanetMemberId",
                table: "notification_subscriptions",
                column: "PlanetMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_notification_subscriptions_planet_members_PlanetMemberId",
                table: "notification_subscriptions",
                column: "PlanetMemberId",
                principalTable: "planet_members",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notification_subscriptions_planet_members_PlanetMemberId",
                table: "notification_subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_notification_subscriptions_PlanetMemberId",
                table: "notification_subscriptions");

            migrationBuilder.RenameColumn(
                name: "PlanetMemberId",
                table: "notification_subscriptions",
                newName: "role_hash_key");

            migrationBuilder.AddColumn<long>(
                name: "member_id",
                table: "notification_subscriptions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "planet_id",
                table: "notification_subscriptions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_subscriptions_member_id",
                table: "notification_subscriptions",
                column: "member_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_subscriptions_planet_id",
                table: "notification_subscriptions",
                column: "planet_id");

            migrationBuilder.AddForeignKey(
                name: "FK_notification_subscriptions_planet_members_member_id",
                table: "notification_subscriptions",
                column: "member_id",
                principalTable: "planet_members",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_notification_subscriptions_planets_planet_id",
                table: "notification_subscriptions",
                column: "planet_id",
                principalTable: "planets",
                principalColumn: "id");
        }
    }
}
