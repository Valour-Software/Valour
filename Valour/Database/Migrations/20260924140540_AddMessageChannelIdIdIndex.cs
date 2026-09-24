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
        // always have an index.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_messages_channel_id_id" ON messages (channel_id, id);""",
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
