using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class ChannelActivityNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "activity_cooldown_seconds",
                table: "user_preferences",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "activity_alerts",
                table: "user_channel_states",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "activity_notification_cadence",
                table: "planets",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            // ChannelActivity (0x20000) must default ON for existing users.
            // Rows with a zero mask fall back to all-on dynamically, but any
            // non-zero mask (initialized or legacy-explicit) would read the
            // missing bit as disabled — flip it on for those.
            migrationBuilder.Sql(
                "UPDATE user_preferences SET enabled_notification_sources = enabled_notification_sources | 131072 WHERE enabled_notification_sources <> 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "activity_cooldown_seconds",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "activity_alerts",
                table: "user_channel_states");

            migrationBuilder.DropColumn(
                name: "activity_notification_cadence",
                table: "planets");
        }
    }
}
