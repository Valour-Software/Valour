using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Valour.Database.Context;

#nullable disable

namespace Valour.Database.Migrations
{
    [DbContext(typeof(ValourDb))]
    [Migration("20260212120000_AddAuthCodeExpirations")]
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
                SET created_at = COALESCE(created_at, NOW() AT TIME ZONE 'utc')
                WHERE created_at IS NULL;

                UPDATE email_confirm_codes
                SET expires_at = COALESCE(expires_at, (NOW() AT TIME ZONE 'utc') + interval '1 day')
                WHERE expires_at IS NULL;

                ALTER TABLE email_confirm_codes ALTER COLUMN created_at SET DEFAULT (NOW() AT TIME ZONE 'utc');
                ALTER TABLE email_confirm_codes ALTER COLUMN expires_at SET DEFAULT ((NOW() AT TIME ZONE 'utc') + interval '1 day');
                ALTER TABLE email_confirm_codes ALTER COLUMN created_at SET NOT NULL;
                ALTER TABLE email_confirm_codes ALTER COLUMN expires_at SET NOT NULL;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE password_recoveries ADD COLUMN IF NOT EXISTS created_at timestamp with time zone;
                ALTER TABLE password_recoveries ADD COLUMN IF NOT EXISTS expires_at timestamp with time zone;

                UPDATE password_recoveries
                SET created_at = COALESCE(created_at, NOW() AT TIME ZONE 'utc')
                WHERE created_at IS NULL;

                UPDATE password_recoveries
                SET expires_at = COALESCE(expires_at, (NOW() AT TIME ZONE 'utc') + interval '1 hour')
                WHERE expires_at IS NULL;

                ALTER TABLE password_recoveries ALTER COLUMN created_at SET DEFAULT (NOW() AT TIME ZONE 'utc');
                ALTER TABLE password_recoveries ALTER COLUMN expires_at SET DEFAULT ((NOW() AT TIME ZONE 'utc') + interval '1 hour');
                ALTER TABLE password_recoveries ALTER COLUMN created_at SET NOT NULL;
                ALTER TABLE password_recoveries ALTER COLUMN expires_at SET NOT NULL;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_email_confirm_codes_expires_at"" ON email_confirm_codes (expires_at);
                CREATE INDEX IF NOT EXISTS ""IX_password_recoveries_expires_at"" ON password_recoveries (expires_at);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: We do not want rollbacks to drop auth columns.
        }
    }
}
