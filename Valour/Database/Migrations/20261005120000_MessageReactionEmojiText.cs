using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class MessageReactionEmojiText : Migration
    {
        // The model has always mapped message_reactions.emoji as text, but
        // databases whose table predates the EF migrations kept an older
        // varchar(32) column, because the table is created with IF NOT EXISTS.
        // Custom emoji tokens with names longer than eight characters do not fit
        // in 32 characters. Converting varchar to text rewrites neither the table
        // nor its indexes, and databases that already use text are left alone.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$ BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'message_reactions'
                          AND column_name = 'emoji'
                          AND data_type <> 'text')
                    THEN
                        ALTER TABLE message_reactions ALTER COLUMN emoji TYPE text;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The model maps the column as text, so there is nothing to restore.
        }
    }
}
