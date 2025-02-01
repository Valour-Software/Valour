using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_referrals_users_referrer_id",
                table: "referrals");

            migrationBuilder.DropTable(
                name: "notification_subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_referrals_referrer_id",
                table: "referrals");

            migrationBuilder.AddColumn<long>(
                name: "UserId1",
                table: "user_emails",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "multi_auth",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "text", nullable: true),
                    secret = table.Column<string>(type: "text", nullable: true),
                    verified = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_multi_auth", x => x.id);
                    table.ForeignKey(
                        name: "FK_multi_auth_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PushNotificationSubscriptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceType = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    PlanetId = table.Column<long>(type: "bigint", nullable: true),
                    MemberId = table.Column<long>(type: "bigint", nullable: true),
                    RoleHashKey = table.Column<long>(type: "bigint", nullable: true),
                    Endpoint = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: true),
                    Auth = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushNotificationSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushNotificationSubscriptions_planets_PlanetId",
                        column: x => x.PlanetId,
                        principalTable: "planets",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_PushNotificationSubscriptions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_emails_UserId1",
                table: "user_emails",
                column: "UserId1");

            migrationBuilder.CreateIndex(
                name: "IX_multi_auth_user_id",
                table: "multi_auth",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_PushNotificationSubscriptions_PlanetId",
                table: "PushNotificationSubscriptions",
                column: "PlanetId");

            migrationBuilder.CreateIndex(
                name: "IX_PushNotificationSubscriptions_UserId",
                table: "PushNotificationSubscriptions",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_user_emails_users_UserId1",
                table: "user_emails",
                column: "UserId1",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_user_emails_users_UserId1",
                table: "user_emails");

            migrationBuilder.DropTable(
                name: "multi_auth");

            migrationBuilder.DropTable(
                name: "PushNotificationSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_user_emails_UserId1",
                table: "user_emails");

            migrationBuilder.DropColumn(
                name: "UserId1",
                table: "user_emails");

            migrationBuilder.CreateTable(
                name: "notification_subscriptions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    auth = table.Column<string>(type: "text", nullable: true),
                    endpoint = table.Column<string>(type: "text", nullable: true),
                    key = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_notification_subscriptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_referrals_referrer_id",
                table: "referrals",
                column: "referrer_id");

            migrationBuilder.CreateIndex(
                name: "IX_notification_subscriptions_user_id",
                table: "notification_subscriptions",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_referrals_users_referrer_id",
                table: "referrals",
                column: "referrer_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
