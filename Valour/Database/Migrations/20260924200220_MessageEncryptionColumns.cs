using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Valour.Database.Context;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <summary>
    /// Adds the encryption columns to messages: the encryption version, the
    /// envelope, the key generation, and the keyed search terms.
    ///
    /// Adding a column with a constant default only changes the table's
    /// definition, but it needs a brief exclusive lock on messages, which waits
    /// behind any long-running query. The statement runs in its own
    /// transaction, apart from the locks earlier migrations took, and gives up
    /// after five seconds instead of queueing every other query on messages
    /// behind it. The migration then runs again on the next start. Columns
    /// that already exist are kept, so running it again is safe.
    /// </summary>
    [DbContext(typeof(ValourDb))]
    [Migration("20260924200220_MessageEncryptionColumns")]
    public partial class MessageEncryptionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    PERFORM set_config('lock_timeout', '5s', true);
                    ALTER TABLE messages
                        ADD COLUMN IF NOT EXISTS encryption_version integer NOT NULL DEFAULT 0,
                        ADD COLUMN IF NOT EXISTS envelope bytea,
                        ADD COLUMN IF NOT EXISTS key_generation integer NOT NULL DEFAULT 0,
                        ADD COLUMN IF NOT EXISTS search_terms integer[];
                END $$;
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "encryption_version",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "envelope",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "key_generation",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "search_terms",
                table: "messages");
        }
    }
}
