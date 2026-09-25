using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Valour.Database.Context;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <summary>
    /// Adds the partial index that finds a channel's server-sealed messages
    /// without search terms, which members' apps index through
    /// api/e2ee/channels/{id}/unindexed. It covers only those messages, so it
    /// stays small once history is indexed.
    ///
    /// The index is built with CREATE INDEX CONCURRENTLY, which cannot run in
    /// a transaction, so this migration holds nothing else. Every statement is
    /// safe to run again: an index left invalid by an interrupted build is
    /// renamed and dropped, and a valid one is kept.
    /// </summary>
    [DbContext(typeof(ValourDb))]
    [Migration("20260925090300_UnindexedSealedMessagesIndex")]
    public partial class UnindexedSealedMessagesIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_messages_unindexed_sealed_invalid;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "DO $$ BEGIN IF EXISTS (SELECT 1 FROM pg_index WHERE indexrelid = to_regclass('ix_messages_unindexed_sealed') AND NOT indisvalid) THEN ALTER INDEX ix_messages_unindexed_sealed RENAME TO ix_messages_unindexed_sealed_invalid; END IF; END $$;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_messages_unindexed_sealed_invalid;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_messages_unindexed_sealed ON messages (channel_id, id) WHERE encryption_version = 2 AND search_terms IS NULL;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_messages_unindexed_sealed;",
                suppressTransaction: true);
        }
    }
}
