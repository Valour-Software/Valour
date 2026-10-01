using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class GooglePlayBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "google_play_expiry",
                table: "user_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "google_play_order_id",
                table: "user_subscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "google_play_purchase_token",
                table: "user_subscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_subscriptions_google_play_purchase_token",
                table: "user_subscriptions",
                column: "google_play_purchase_token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_subscriptions_google_play_purchase_token",
                table: "user_subscriptions");

            migrationBuilder.DropColumn(
                name: "google_play_expiry",
                table: "user_subscriptions");

            migrationBuilder.DropColumn(
                name: "google_play_order_id",
                table: "user_subscriptions");

            migrationBuilder.DropColumn(
                name: "google_play_purchase_token",
                table: "user_subscriptions");
        }
    }
}
