using Microsoft.EntityFrameworkCore.Migrations;
using Valour.Database.Seeds.Villages;

#nullable disable

namespace Valour.Database.Migrations;

public partial class SeedVillageDefaultWorld : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var json = VillageDefaultWorld.Version1Json.Replace("'", "''");
        migrationBuilder.Sql($"""
            INSERT INTO village_template (id, revision, reset_before_revision, published_json)
            VALUES (1, 1, 1, '{json}')
            ON CONFLICT (id) DO UPDATE SET published_json = EXCLUDED.published_json
            WHERE village_template.published_json IS NULL
              AND village_template.published_at IS NULL
              AND village_template.published_by_user_id IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        var json = VillageDefaultWorld.Version1Json.Replace("'", "''");
        migrationBuilder.Sql($"""
            UPDATE village_template SET published_json = NULL
            WHERE id = 1 AND published_json = '{json}'
              AND published_at IS NULL AND published_by_user_id IS NULL;
            """);
    }
}
