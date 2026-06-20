using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class DropLocality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_emails_locality",
                table: "user_emails");

            migrationBuilder.DropColumn(
                name: "locality",
                table: "user_emails");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "locality",
                table: "user_emails",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_emails_locality",
                table: "user_emails",
                column: "locality");
        }
    }
}
