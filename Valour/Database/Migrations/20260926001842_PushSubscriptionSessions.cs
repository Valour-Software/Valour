using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <summary>
    /// Links each push subscription to the session that registered it, so
    /// ending the session deletes the subscription. Push payloads carry
    /// encrypted messages that the device can decrypt with keys it kept, so a
    /// device whose session was revoked must stop receiving them. Existing
    /// subscriptions have no session until their app registers again.
    /// </summary>
    public partial class PushSubscriptionSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "auth_token_id",
                table: "notification_subscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_subscriptions_auth_token_id",
                table: "notification_subscriptions",
                column: "auth_token_id");

            migrationBuilder.AddForeignKey(
                name: "FK_notification_subscriptions_auth_tokens_auth_token_id",
                table: "notification_subscriptions",
                column: "auth_token_id",
                principalTable: "auth_tokens",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notification_subscriptions_auth_tokens_auth_token_id",
                table: "notification_subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_notification_subscriptions_auth_token_id",
                table: "notification_subscriptions");

            migrationBuilder.DropColumn(
                name: "auth_token_id",
                table: "notification_subscriptions");
        }
    }
}
