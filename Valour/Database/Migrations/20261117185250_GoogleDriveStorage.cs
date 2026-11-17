using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class GoogleDriveStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "drive_attempts",
                table: "cdn_bucket_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "drive_error",
                table: "cdn_bucket_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "drive_file_id",
                table: "cdn_bucket_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "drive_next_attempt_at",
                table: "cdn_bucket_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "drive_state",
                table: "cdn_bucket_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "drive_synced_at",
                table: "cdn_bucket_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "storage_expires_at",
                table: "cdn_bucket_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "storage_purged_at",
                table: "cdn_bucket_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_drive_connections",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    google_account_id = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    refresh_token_encrypted = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    connected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_sync_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_drive_connections", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "user_drive_folders",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    drive_folder_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_drive_folders", x => new { x.user_id, x.key });
                });

            migrationBuilder.CreateIndex(
                name: "IX_cdn_bucket_items_drive_state",
                table: "cdn_bucket_items",
                column: "drive_state",
                filter: "drive_state IN (1, 2, 4)");

            migrationBuilder.CreateIndex(
                name: "IX_cdn_bucket_items_storage_expires_at",
                table: "cdn_bucket_items",
                column: "storage_expires_at",
                filter: "storage_expires_at IS NOT NULL AND storage_purged_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_drive_connections");

            migrationBuilder.DropTable(
                name: "user_drive_folders");

            migrationBuilder.DropIndex(
                name: "IX_cdn_bucket_items_drive_state",
                table: "cdn_bucket_items");

            migrationBuilder.DropIndex(
                name: "IX_cdn_bucket_items_storage_expires_at",
                table: "cdn_bucket_items");

            migrationBuilder.DropColumn(
                name: "drive_attempts",
                table: "cdn_bucket_items");

            migrationBuilder.DropColumn(
                name: "drive_error",
                table: "cdn_bucket_items");

            migrationBuilder.DropColumn(
                name: "drive_file_id",
                table: "cdn_bucket_items");

            migrationBuilder.DropColumn(
                name: "drive_next_attempt_at",
                table: "cdn_bucket_items");

            migrationBuilder.DropColumn(
                name: "drive_state",
                table: "cdn_bucket_items");

            migrationBuilder.DropColumn(
                name: "drive_synced_at",
                table: "cdn_bucket_items");

            migrationBuilder.DropColumn(
                name: "storage_expires_at",
                table: "cdn_bucket_items");

            migrationBuilder.DropColumn(
                name: "storage_purged_at",
                table: "cdn_bucket_items");
        }
    }
}
