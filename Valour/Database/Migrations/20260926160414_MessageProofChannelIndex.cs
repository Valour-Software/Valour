using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class MessageProofChannelIndex : Migration
    {
        // Proofs are deleted by channel when channels are removed, for example
        // during account deletion. message_proofs gains a row for every sealed
        // message revision and migrations run at server startup, so the index is
        // built concurrently to avoid blocking message writes. CONCURRENTLY cannot
        // run inside a transaction, so each statement suppresses the migration
        // transaction. An index left invalid by an interrupted build is renamed
        // and dropped first, so every statement is safe to run again.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_message_proofs_channel_id_invalid";""",
                suppressTransaction: true);

            migrationBuilder.Sql(
                """DO $$ BEGIN IF EXISTS (SELECT 1 FROM pg_index WHERE indexrelid = to_regclass('"IX_message_proofs_channel_id"') AND NOT indisvalid) THEN ALTER INDEX "IX_message_proofs_channel_id" RENAME TO "IX_message_proofs_channel_id_invalid"; END IF; END $$;""",
                suppressTransaction: true);

            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_message_proofs_channel_id_invalid";""",
                suppressTransaction: true);

            migrationBuilder.Sql(
                """CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_message_proofs_channel_id" ON message_proofs (channel_id);""",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_message_proofs_channel_id";""",
                suppressTransaction: true);
        }
    }
}
