using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class UniqueMessageReactionPerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve the oldest row for each logical reaction before the
            // uniqueness constraint is applied. Older deployments may contain
            // duplicates created by concurrent reaction requests.
            migrationBuilder.Sql(
                """
                DELETE FROM message_reactions AS duplicate
                USING message_reactions AS keeper
                WHERE duplicate.message_id = keeper.message_id
                  AND duplicate.author_user_id = keeper.author_user_id
                  AND duplicate.emoji = keeper.emoji
                  AND duplicate.id > keeper.id;
                """);

            migrationBuilder.DropIndex(
                name: "IX_message_reactions_message_id",
                table: "message_reactions");

            migrationBuilder.CreateIndex(
                name: "IX_message_reactions_message_id_author_user_id_emoji",
                table: "message_reactions",
                columns: new[] { "message_id", "author_user_id", "emoji" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_message_reactions_message_id_author_user_id_emoji",
                table: "message_reactions");

            migrationBuilder.CreateIndex(
                name: "IX_message_reactions_message_id",
                table: "message_reactions",
                column: "message_id");
        }
    }
}
