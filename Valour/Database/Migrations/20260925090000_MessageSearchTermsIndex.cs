using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Valour.Database.Context;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <summary>
    /// Adds the GIN index that encrypted search and automod use to match keyed
    /// search terms with @>. It covers only messages that have terms.
    ///
    /// The index is built with CREATE INDEX CONCURRENTLY, which cannot run in
    /// a transaction, so this migration holds nothing else. Every statement is
    /// safe to run again: an index left invalid by an interrupted build is
    /// renamed and dropped, and a valid one is kept.
    /// </summary>
    [DbContext(typeof(ValourDb))]
    [Migration("20260925090000_MessageSearchTermsIndex")]
    public partial class MessageSearchTermsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_messages_search_terms_invalid;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "DO $$ BEGIN IF EXISTS (SELECT 1 FROM pg_index WHERE indexrelid = to_regclass('ix_messages_search_terms') AND NOT indisvalid) THEN ALTER INDEX ix_messages_search_terms RENAME TO ix_messages_search_terms_invalid; END IF; END $$;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_messages_search_terms_invalid;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_messages_search_terms ON messages USING gin (search_terms) WHERE search_terms IS NOT NULL;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_messages_search_terms;",
                suppressTransaction: true);
        }
    }
}
