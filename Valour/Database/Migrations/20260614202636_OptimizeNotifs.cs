using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeNotifs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_notifications_time_read",
                table: "notifications",
                column: "time_read");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_user_id_time_sent",
                table: "notifications",
                columns: new[] { "user_id", "time_sent" },
                filter: "time_read IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_time_read",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_user_id_time_sent",
                table: "notifications");
        }
    }
}
