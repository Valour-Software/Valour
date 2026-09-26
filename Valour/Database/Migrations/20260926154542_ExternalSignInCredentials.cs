using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class ExternalSignInCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "credentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "credentials",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_used_at",
                table: "credentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_credentials_credential_type_identifier",
                table: "credentials",
                columns: new[] { "credential_type", "identifier" },
                unique: true,
                filter: "credential_type <> 'Password'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_credentials_credential_type_identifier",
                table: "credentials");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "credentials");

            migrationBuilder.DropColumn(
                name: "display_name",
                table: "credentials");

            migrationBuilder.DropColumn(
                name: "last_used_at",
                table: "credentials");
        }
    }
}
