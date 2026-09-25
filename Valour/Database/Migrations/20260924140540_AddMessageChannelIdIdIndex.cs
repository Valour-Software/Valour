using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageChannelIdIdIndex : Migration
    {
        // messages is the largest table, and migrations run at server startup. The
        // indexes are built and dropped concurrently so message writes are never
        // blocked for the duration of the build. CONCURRENTLY cannot run inside a
        // transaction, so each statement suppresses the migration transaction. The
        // new index is created before the old one is dropped so channel lookups
        // always have an index. An index left invalid by an interrupted build is
        // renamed and dropped before building, and the old index is dropped only
        // when the new one is valid, so a rerun never leaves channels unindexed.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_messages_channel_id_id_invalid";""",
                suppressTransaction: true);

            migrationBuilder.Sql(
                """DO $$ BEGIN IF EXISTS (SELECT 1 FROM pg_index WHERE indexrelid = to_regclass('"IX_messages_channel_id_id"') AND NOT indisvalid) THEN ALTER INDEX "IX_messages_channel_id_id" RENAME TO "IX_messages_channel_id_id_invalid"; END IF; END $$;""",
                suppressTransaction: true);

            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_messages_channel_id_id_invalid";""",
                suppressTransaction: true);

            migrationBuilder.Sql(
                """CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_messages_channel_id_id" ON messages (channel_id, id);""",
                suppressTransaction: true);

            // Fails the migration instead of dropping the only channel index
            // when the new index is missing or invalid.
            migrationBuilder.Sql(
                """DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_index WHERE indexrelid = to_regclass('"IX_messages_channel_id_id"') AND indisvalid) THEN RAISE EXCEPTION 'IX_messages_channel_id_id is not valid; rerun the migration'; END IF; END $$;""",
                suppressTransaction: true);

            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_messages_channel_id";""",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_messages_channel_id" ON messages (channel_id);""",
                suppressTransaction: true);

            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_messages_channel_id_id";""",
                suppressTransaction: true);
        }
    }
}
