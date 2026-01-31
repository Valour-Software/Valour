using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddReportResolutionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL with IF NOT EXISTS for idempotent migrations

            // Add columns to reports table (only if they don't exist)
            migrationBuilder.Sql(@"
                ALTER TABLE reports ADD COLUMN IF NOT EXISTS reported_user_id bigint;
                ALTER TABLE reports ADD COLUMN IF NOT EXISTS resolution integer NOT NULL DEFAULT 0;
                ALTER TABLE reports ADD COLUMN IF NOT EXISTS resolved_at timestamp with time zone;
                ALTER TABLE reports ADD COLUMN IF NOT EXISTS resolved_by_id bigint;
                ALTER TABLE reports ADD COLUMN IF NOT EXISTS staff_notes text;
            ");

            // Add owner_id to users table (only if it doesn't exist)
            migrationBuilder.Sql(@"
                ALTER TABLE users ADD COLUMN IF NOT EXISTS owner_id bigint;
            ");

            // Alter planet_members rf columns to be non-nullable with default
            migrationBuilder.Sql(@"
                ALTER TABLE planet_members ALTER COLUMN rf0 SET NOT NULL;
                ALTER TABLE planet_members ALTER COLUMN rf0 SET DEFAULT 0;
                ALTER TABLE planet_members ALTER COLUMN rf1 SET NOT NULL;
                ALTER TABLE planet_members ALTER COLUMN rf1 SET DEFAULT 0;
                ALTER TABLE planet_members ALTER COLUMN rf2 SET NOT NULL;
                ALTER TABLE planet_members ALTER COLUMN rf2 SET DEFAULT 0;
                ALTER TABLE planet_members ALTER COLUMN rf3 SET NOT NULL;
                ALTER TABLE planet_members ALTER COLUMN rf3 SET DEFAULT 0;
            ");

            // Create indexes (IF NOT EXISTS)
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_reports_reported_user_id"" ON reports (reported_user_id);
                CREATE INDEX IF NOT EXISTS ""IX_reports_resolution"" ON reports (resolution);
                CREATE INDEX IF NOT EXISTS ""IX_reports_resolved_by_id"" ON reports (resolved_by_id);
                CREATE INDEX IF NOT EXISTS ""IX_users_owner_id"" ON users (owner_id);
            ");

            // Add foreign key for users.owner_id (only if it doesn't exist)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.table_constraints
                        WHERE constraint_name = 'FK_users_users_owner_id'
                    ) THEN
                        ALTER TABLE users ADD CONSTRAINT ""FK_users_users_owner_id""
                            FOREIGN KEY (owner_id) REFERENCES users(id) ON DELETE SET NULL;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: We don't want to drop columns on rollback
        }
    }
}
