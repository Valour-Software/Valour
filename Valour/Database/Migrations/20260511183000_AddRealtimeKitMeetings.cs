using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Valour.Database.Context;

#nullable disable

namespace Valour.Database.Migrations;

[DbContext(typeof(ValourDb))]
[Migration("20260511183000_AddRealtimeKitMeetings")]
public partial class AddRealtimeKitMeetings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "realtimekit_meetings",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                channel_id = table.Column<long>(type: "bigint", nullable: false),
                planet_id = table.Column<long>(type: "bigint", nullable: true),
                meeting_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                last_cleanup_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                cleanup_failure_count = table.Column<int>(type: "integer", nullable: false),
                last_cleanup_error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_realtimekit_meetings", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_realtimekit_meetings_channel_id",
            table: "realtimekit_meetings",
            column: "channel_id",
            unique: true,
            filter: "closed_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_realtimekit_meetings_meeting_id",
            table: "realtimekit_meetings",
            column: "meeting_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_realtimekit_meetings_status_closed_at",
            table: "realtimekit_meetings",
            columns: new[] { "status", "closed_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "realtimekit_meetings");
    }
}
