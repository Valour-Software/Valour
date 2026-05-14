using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class RTKMeetingTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // RealtimeKit meeting schema is created by 20260511183000_AddRealtimeKitMeetings.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Keep rollback ownership with 20260511183000_AddRealtimeKitMeetings.
        }
    }
}
