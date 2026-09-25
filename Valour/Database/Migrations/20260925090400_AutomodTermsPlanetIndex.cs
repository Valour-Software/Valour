using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Valour.Database.Context;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <summary>
    /// Adds the index on encrypted automod terms by planet, which moving a
    /// planet between nodes uses to copy and remove its terms. The table holds
    /// a few rows per trigger and channel, so the index is built directly. An
    /// index that already exists is kept, so running it again is safe.
    /// </summary>
    [DbContext(typeof(ValourDb))]
    [Migration("20260925090400_AutomodTermsPlanetIndex")]
    public partial class AutomodTermsPlanetIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_e2ee_automod_terms_planet_id\" ON e2ee_automod_terms (planet_id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_e2ee_automod_terms_planet_id\";");
        }
    }
}
