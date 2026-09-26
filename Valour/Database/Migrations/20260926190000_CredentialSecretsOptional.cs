using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class CredentialSecretsOptional : Migration
    {
        // Google, Discord, and device key credentials have no salt, and linked
        // accounts have no secret. The model has always allowed null here, but
        // databases created before migrations have NOT NULL on both columns, so
        // no generated migration ever changed them. Dropping NOT NULL is a
        // metadata change and does nothing where the columns already allow null.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE credentials ALTER COLUMN secret DROP NOT NULL;""");
            migrationBuilder.Sql("""ALTER TABLE credentials ALTER COLUMN salt DROP NOT NULL;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rows without a salt or secret may exist by now, so NOT NULL is
            // not restored.
        }
    }
}
