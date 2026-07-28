using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class DashboardAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_activity_days",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_activity_days", x => new { x.day, x.user_id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_stat_objects_time_created",
                table: "stat_objects",
                column: "time_created");

            migrationBuilder.CreateIndex(
                name: "IX_messages_time_sent",
                table: "messages",
                column: "time_sent");

            migrationBuilder.CreateIndex(
                name: "IX_user_activity_days_day",
                table: "user_activity_days",
                column: "day");

            // One-time backfill from message history so the analytics charts
            // aren't empty on day one. A sequential scan here is acceptable.
            migrationBuilder.Sql(
                "INSERT INTO user_activity_days (day, user_id) SELECT DISTINCT time_sent::date, author_user_id FROM messages ON CONFLICT DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_activity_days");

            migrationBuilder.DropIndex(
                name: "IX_stat_objects_time_created",
                table: "stat_objects");

            migrationBuilder.DropIndex(
                name: "IX_messages_time_sent",
                table: "messages");
        }
    }
}
