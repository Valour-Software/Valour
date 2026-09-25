using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <summary>
    /// Makes every invite-only planet private. Whether a planet is public now
    /// decides its encryption mode, and an invite-only planet only becomes
    /// public through an entry its owner signs in the membership log, which
    /// the server cannot write. Private planets that are still open stay as
    /// they are until the owner's device finishes making them private.
    ///
    /// A planet that is not public had invites turned off, and its owner was
    /// told that existing links stop working. Those links would now let people
    /// into a planet whose privacy is not finished, so they are deleted.
    /// </summary>
    public partial class PrivatePlanetsAreNotPublic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Changes only the few planets that are both public and
            // invite-only, and the invites of planets that are not public.
            // Give up instead of queueing queries on planets
            // behind a long-running one; the migration runs again on the
            // next start.
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");
            migrationBuilder.Sql(
                "UPDATE planets SET public = false, vanity_invite_enabled = false " +
                "WHERE public AND encryption_mode = 1;");
            migrationBuilder.Sql(
                "DELETE FROM planet_invites WHERE planet_id IN " +
                "(SELECT id FROM planets WHERE NOT public AND encryption_mode = 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Which of these planets were public before is not recorded, and
            // an invite-only planet must not be public, so nothing is undone.
        }
    }
}
