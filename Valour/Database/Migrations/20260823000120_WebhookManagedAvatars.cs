using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class WebhookManagedAvatars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "avatar_url",
                table: "planet_webhooks");

            migrationBuilder.DropColumn(
                name: "override_avatar_url",
                table: "messages");

            migrationBuilder.AddColumn<bool>(
                name: "avatar_animated",
                table: "planet_webhooks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "avatar_asset_id",
                table: "planet_webhooks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "webhook_avatar_animated",
                table: "messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "webhook_avatar_asset_id",
                table: "messages",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "avatar_animated",
                table: "planet_webhooks");

            migrationBuilder.DropColumn(
                name: "avatar_asset_id",
                table: "planet_webhooks");

            migrationBuilder.DropColumn(
                name: "webhook_avatar_animated",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "webhook_avatar_asset_id",
                table: "messages");

            migrationBuilder.AddColumn<string>(
                name: "avatar_url",
                table: "planet_webhooks",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "override_avatar_url",
                table: "messages",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }
    }
}
