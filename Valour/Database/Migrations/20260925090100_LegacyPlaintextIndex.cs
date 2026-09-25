using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Valour.Database.Context;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <summary>
    /// Adds the partial index the legacy sealing worker uses to find the next
    /// channel with plain-text messages and to read them in id order.
    ///
    /// The index is built with CREATE INDEX CONCURRENTLY, which cannot run in
    /// a transaction, so this migration holds nothing else. Every statement is
    /// safe to run again: an index left invalid by an interrupted build is
    /// renamed and dropped, and a valid one is kept.
    /// </summary>
    [DbContext(typeof(ValourDb))]
    [Migration("20260925090100_LegacyPlaintextIndex")]
    public partial class LegacyPlaintextIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_messages_legacy_plaintext_invalid;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "DO $$ BEGIN IF EXISTS (SELECT 1 FROM pg_index WHERE indexrelid = to_regclass('ix_messages_legacy_plaintext') AND NOT indisvalid) THEN ALTER INDEX ix_messages_legacy_plaintext RENAME TO ix_messages_legacy_plaintext_invalid; END IF; END $$;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_messages_legacy_plaintext_invalid;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_messages_legacy_plaintext ON messages (channel_id, id) WHERE encryption_version = 0;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_messages_legacy_plaintext;",
                suppressTransaction: true);
        }
    }
}
