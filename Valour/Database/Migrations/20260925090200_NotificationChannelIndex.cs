using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Valour.Database.Context;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <summary>
    /// Adds an index on notifications by channel, which the legacy sealing
    /// worker uses to replace message text copied into notifications.
    ///
    /// The index is built with CREATE INDEX CONCURRENTLY, which cannot run in
    /// a transaction, so this migration holds nothing else. Every statement is
    /// safe to run again: an index left invalid by an interrupted build is
    /// renamed and dropped, and a valid one is kept.
    /// </summary>
    [DbContext(typeof(ValourDb))]
    [Migration("20260925090200_NotificationChannelIndex")]
    public partial class NotificationChannelIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_notifications_channel_id_invalid;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "DO $$ BEGIN IF EXISTS (SELECT 1 FROM pg_index WHERE indexrelid = to_regclass('ix_notifications_channel_id') AND NOT indisvalid) THEN ALTER INDEX ix_notifications_channel_id RENAME TO ix_notifications_channel_id_invalid; END IF; END $$;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_notifications_channel_id_invalid;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_notifications_channel_id ON notifications (channel_id) WHERE channel_id IS NOT NULL;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_notifications_channel_id;",
                suppressTransaction: true);
        }
    }
}
