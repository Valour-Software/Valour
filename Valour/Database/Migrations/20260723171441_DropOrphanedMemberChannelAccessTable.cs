using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class DropOrphanedMemberChannelAccessTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Orphaned table: its entity was removed in Dec 2024 without a drop
            // migration, so it only exists on databases created before then. Its
            // stale FK rows blocked planet_members deletes during user hard-deletes.
            migrationBuilder.Sql("DROP TABLE IF EXISTS member_channel_access;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not restorable: the table predates the current model and held only stale data.
        }
    }
}
