using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class AppStoreBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "app_store_expiry",
                table: "user_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "app_store_original_transaction_id",
                table: "user_subscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "app_store_transaction_id",
                table: "user_subscriptions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_subscriptions_app_store_original_transaction_id",
                table: "user_subscriptions",
                column: "app_store_original_transaction_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_subscriptions_app_store_original_transaction_id",
                table: "user_subscriptions");

            migrationBuilder.DropColumn(
                name: "app_store_expiry",
                table: "user_subscriptions");

            migrationBuilder.DropColumn(
                name: "app_store_original_transaction_id",
                table: "user_subscriptions");

            migrationBuilder.DropColumn(
                name: "app_store_transaction_id",
                table: "user_subscriptions");
        }
    }
}
