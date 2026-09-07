using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Valour.Database.Context;

namespace Valour.Database.Migrations;

[DbContext(typeof(ValourDb))]
[Migration("20260907160000_NormalizePlanetRoleTextColumns")]
public class NormalizePlanetRoleTextColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // CREATE TABLE IF NOT EXISTS in the refactor migration preserves legacy varchar limits.
        migrationBuilder.Sql("ALTER TABLE planet_roles ALTER COLUMN name TYPE text, ALTER COLUMN color TYPE text;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Text is also the preceding EF model's type. Restoring legacy limits could reject stored names.
    }
}
