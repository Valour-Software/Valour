using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class GoogleDriveDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "auto_sync_from",
                table: "user_drive_connections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "drive_release_on_sync",
                table: "cdn_bucket_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Connections made before this setting existed sent new uploads to Drive.
            migrationBuilder.Sql("UPDATE user_drive_connections SET auto_sync_from = connected_at WHERE auto_sync_from IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_sync_from",
                table: "user_drive_connections");

            migrationBuilder.DropColumn(
                name: "drive_release_on_sync",
                table: "cdn_bucket_items");
        }
    }
}
