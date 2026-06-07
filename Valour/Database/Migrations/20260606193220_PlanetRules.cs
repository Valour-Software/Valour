using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class PlanetRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "planet_rules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planet_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_planet_rules_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_planet_rules_planet_id",
                table: "planet_rules",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_rules_planet_id_id",
                table: "planet_rules",
                columns: new[] { "planet_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_planet_rules_planet_id_position",
                table: "planet_rules",
                columns: new[] { "planet_id", "position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "planet_rules");
        }
    }
}
