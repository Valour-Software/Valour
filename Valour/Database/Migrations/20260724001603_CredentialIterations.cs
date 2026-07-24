using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class CredentialIterations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows were hashed at the original 30,000 iterations. They
            // are backfilled with that count so verification stays correct, and
            // are re-hashed at the current count on next successful login.
            migrationBuilder.AddColumn<int>(
                name: "iterations",
                table: "credentials",
                type: "integer",
                nullable: false,
                defaultValue: 30000);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "iterations",
                table: "credentials");
        }
    }
}
