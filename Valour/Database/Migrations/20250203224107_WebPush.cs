using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class WebPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PushNotificationSubscriptions_planets_PlanetId",
                table: "PushNotificationSubscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_PushNotificationSubscriptions_users_UserId",
                table: "PushNotificationSubscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_user_emails_users_UserId1",
                table: "user_emails");

            migrationBuilder.DropIndex(
                name: "IX_user_emails_UserId1",
                table: "user_emails");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PushNotificationSubscriptions",
                table: "PushNotificationSubscriptions");

            migrationBuilder.DropColumn(
                name: "UserId1",
                table: "user_emails");

            migrationBuilder.RenameTable(
                name: "PushNotificationSubscriptions",
                newName: "notification_subscriptions");

            migrationBuilder.RenameColumn(
                name: "Key",
                table: "notification_subscriptions",
                newName: "key");

            migrationBuilder.RenameColumn(
                name: "Endpoint",
                table: "notification_subscriptions",
                newName: "endpoint");

            migrationBuilder.RenameColumn(
                name: "Auth",
                table: "notification_subscriptions",
                newName: "auth");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "notification_subscriptions",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "notification_subscriptions",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "RoleHashKey",
                table: "notification_subscriptions",
                newName: "role_hash_key");

            migrationBuilder.RenameColumn(
                name: "PlanetId",
                table: "notification_subscriptions",
                newName: "planet_id");

            migrationBuilder.RenameColumn(
                name: "MemberId",
                table: "notification_subscriptions",
                newName: "member_id");

            migrationBuilder.RenameColumn(
                name: "ExpiresAt",
                table: "notification_subscriptions",
                newName: "expires_at");

            migrationBuilder.RenameColumn(
                name: "DeviceType",
                table: "notification_subscriptions",
                newName: "device_type");

            migrationBuilder.RenameIndex(
                name: "IX_PushNotificationSubscriptions_UserId",
                table: "notification_subscriptions",
                newName: "IX_notification_subscriptions_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_PushNotificationSubscriptions_PlanetId",
                table: "notification_subscriptions",
                newName: "IX_notification_subscriptions_planet_id");

            migrationBuilder.AlterColumn<DateTime>(
                name: "expires_at",
                table: "notification_subscriptions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "(NOW() + INTERVAL '7 days')",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddPrimaryKey(
                name: "PK_notification_subscriptions",
                table: "notification_subscriptions",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "IX_user_emails_birth_date",
                table: "user_emails",
                column: "birth_date");

            migrationBuilder.CreateIndex(
                name: "IX_user_emails_email",
                table: "user_emails",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "IX_user_emails_join_invite_code",
                table: "user_emails",
                column: "join_invite_code");

            migrationBuilder.CreateIndex(
                name: "IX_user_emails_locality",
                table: "user_emails",
                column: "locality");

            migrationBuilder.CreateIndex(
                name: "IX_notification_subscriptions_member_id",
                table: "notification_subscriptions",
                column: "member_id",
                unique: true);

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

            migrationBuilder.AddForeignKey(
                name: "FK_notification_subscriptions_users_user_id",
                table: "notification_subscriptions",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notification_subscriptions_planet_members_member_id",
                table: "notification_subscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_notification_subscriptions_planets_planet_id",
                table: "notification_subscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_notification_subscriptions_users_user_id",
                table: "notification_subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_user_emails_birth_date",
                table: "user_emails");

            migrationBuilder.DropIndex(
                name: "IX_user_emails_email",
                table: "user_emails");

            migrationBuilder.DropIndex(
                name: "IX_user_emails_join_invite_code",
                table: "user_emails");

            migrationBuilder.DropIndex(
                name: "IX_user_emails_locality",
                table: "user_emails");

            migrationBuilder.DropPrimaryKey(
                name: "PK_notification_subscriptions",
                table: "notification_subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_notification_subscriptions_member_id",
                table: "notification_subscriptions");

            migrationBuilder.RenameTable(
                name: "notification_subscriptions",
                newName: "PushNotificationSubscriptions");

            migrationBuilder.RenameColumn(
                name: "key",
                table: "PushNotificationSubscriptions",
                newName: "Key");

            migrationBuilder.RenameColumn(
                name: "endpoint",
                table: "PushNotificationSubscriptions",
                newName: "Endpoint");

            migrationBuilder.RenameColumn(
                name: "auth",
                table: "PushNotificationSubscriptions",
                newName: "Auth");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "PushNotificationSubscriptions",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "PushNotificationSubscriptions",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "role_hash_key",
                table: "PushNotificationSubscriptions",
                newName: "RoleHashKey");

            migrationBuilder.RenameColumn(
                name: "planet_id",
                table: "PushNotificationSubscriptions",
                newName: "PlanetId");

            migrationBuilder.RenameColumn(
                name: "member_id",
                table: "PushNotificationSubscriptions",
                newName: "MemberId");

            migrationBuilder.RenameColumn(
                name: "expires_at",
                table: "PushNotificationSubscriptions",
                newName: "ExpiresAt");

            migrationBuilder.RenameColumn(
                name: "device_type",
                table: "PushNotificationSubscriptions",
                newName: "DeviceType");

            migrationBuilder.RenameIndex(
                name: "IX_notification_subscriptions_user_id",
                table: "PushNotificationSubscriptions",
                newName: "IX_PushNotificationSubscriptions_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_notification_subscriptions_planet_id",
                table: "PushNotificationSubscriptions",
                newName: "IX_PushNotificationSubscriptions_PlanetId");

            migrationBuilder.AddColumn<long>(
                name: "UserId1",
                table: "user_emails",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExpiresAt",
                table: "PushNotificationSubscriptions",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "(NOW() + INTERVAL '7 days')");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PushNotificationSubscriptions",
                table: "PushNotificationSubscriptions",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_user_emails_UserId1",
                table: "user_emails",
                column: "UserId1");

            migrationBuilder.AddForeignKey(
                name: "FK_PushNotificationSubscriptions_planets_PlanetId",
                table: "PushNotificationSubscriptions",
                column: "PlanetId",
                principalTable: "planets",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_PushNotificationSubscriptions_users_UserId",
                table: "PushNotificationSubscriptions",
                column: "UserId",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_emails_users_UserId1",
                table: "user_emails",
                column: "UserId1",
                principalTable: "users",
                principalColumn: "id");
        }
    }
}
