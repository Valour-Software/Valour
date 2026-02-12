using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthCodeExpirations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE email_confirm_codes ADD COLUMN IF NOT EXISTS created_at timestamp with time zone;
                ALTER TABLE email_confirm_codes ADD COLUMN IF NOT EXISTS expires_at timestamp with time zone;

                UPDATE email_confirm_codes
                SET created_at = NOW() AT TIME ZONE 'utc'
                WHERE created_at IS NULL;

                UPDATE email_confirm_codes
                SET expires_at = (NOW() AT TIME ZONE 'utc') + interval '1 day'
                WHERE expires_at IS NULL;

                ALTER TABLE email_confirm_codes ALTER COLUMN created_at SET NOT NULL;
                ALTER TABLE email_confirm_codes ALTER COLUMN expires_at SET NOT NULL;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE password_recoveries ADD COLUMN IF NOT EXISTS created_at timestamp with time zone;
                ALTER TABLE password_recoveries ADD COLUMN IF NOT EXISTS expires_at timestamp with time zone;

                UPDATE password_recoveries
                SET created_at = NOW() AT TIME ZONE 'utc'
                WHERE created_at IS NULL;

                UPDATE password_recoveries
                SET expires_at = (NOW() AT TIME ZONE 'utc') + interval '1 hour'
                WHERE expires_at IS NULL;

                ALTER TABLE password_recoveries ALTER COLUMN created_at SET NOT NULL;
                ALTER TABLE password_recoveries ALTER COLUMN expires_at SET NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                table: "password_recoveries");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "password_recoveries");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "email_confirm_codes");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "email_confirm_codes");
        }
    }
}
