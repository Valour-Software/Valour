using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class MessageReplyOnDeleteSetNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_messages_messages_reply_to_id",
                table: "messages");

            migrationBuilder.AddForeignKey(
                name: "FK_messages_messages_reply_to_id",
                table: "messages",
                column: "reply_to_id",
                principalTable: "messages",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_messages_messages_reply_to_id",
                table: "messages");

            migrationBuilder.AddForeignKey(
                name: "FK_messages_messages_reply_to_id",
                table: "messages",
                column: "reply_to_id",
                principalTable: "messages",
                principalColumn: "id");
        }
    }
}
