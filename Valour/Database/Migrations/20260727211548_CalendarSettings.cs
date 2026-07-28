using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class CalendarSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "enable_calendar",
                table: "planets",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "reminder_minutes",
                table: "planet_event_rsvps",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reminder_sent_at",
                table: "planet_event_rsvps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_planet_event_rsvps_reminder_sent_at",
                table: "planet_event_rsvps",
                column: "reminder_sent_at",
                filter: "reminder_minutes IS NOT NULL AND reminder_sent_at IS NULL");

            // EventReminder (0x40000) must default ON for existing users.
            // Rows with a zero mask fall back to all-on dynamically, but any
            // non-zero mask (initialized or legacy-explicit) would read the
            // missing bit as disabled — flip it on for those.
            migrationBuilder.Sql(
                "UPDATE user_preferences SET enabled_notification_sources = enabled_notification_sources | 262144 WHERE enabled_notification_sources <> 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_planet_event_rsvps_reminder_sent_at",
                table: "planet_event_rsvps");

            migrationBuilder.DropColumn(
                name: "enable_calendar",
                table: "planets");

            migrationBuilder.DropColumn(
                name: "reminder_minutes",
                table: "planet_event_rsvps");

            migrationBuilder.DropColumn(
                name: "reminder_sent_at",
                table: "planet_event_rsvps");
        }
    }
}
