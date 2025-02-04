using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class PushNotif3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notification_subscriptions_planet_members_PlanetMemberId",
                table: "notification_subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_notification_subscriptions_PlanetMemberId",
                table: "notification_subscriptions");

            migrationBuilder.DropColumn(
                name: "PlanetMemberId",
                table: "notification_subscriptions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PlanetMemberId",
                table: "notification_subscriptions",
                type: "bigint",
                nullable: true);

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
    }
}
