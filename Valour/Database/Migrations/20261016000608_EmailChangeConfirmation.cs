using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    public partial class EmailChangeConfirmation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "new_email",
                table: "email_confirm_codes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "session_id",
                table: "email_confirm_codes",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "new_email",
                table: "email_confirm_codes");

            migrationBuilder.DropColumn(
                name: "session_id",
                table: "email_confirm_codes");
        }
    }
}
