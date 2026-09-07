using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class VillageWorld : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "village_buildings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    map_id = table.Column<long>(type: "bigint", nullable: false),
                    interior_map_id = table.Column<long>(type: "bigint", nullable: true),
                    plot_id = table.Column<long>(type: "bigint", nullable: true),
                    channel_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    x = table.Column<int>(type: "integer", nullable: false),
                    y = table.Column<int>(type: "integer", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    door_x = table.Column<int>(type: "integer", nullable: false),
                    door_y = table.Column<int>(type: "integer", nullable: false),
                    sprite_key = table.Column<string>(type: "text", nullable: true),
                    owner_member_id = table.Column<long>(type: "bigint", nullable: true),
                    voice_mode = table.Column<int>(type: "integer", nullable: false),
                    for_sale = table.Column<bool>(type: "boolean", nullable: false),
                    price = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_village_buildings", x => x.id);
                    table.ForeignKey(
                        name: "FK_village_buildings_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "village_map_chunks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    map_id = table.Column<long>(type: "bigint", nullable: false),
                    chunk_x = table.Column<int>(type: "integer", nullable: false),
                    chunk_y = table.Column<int>(type: "integer", nullable: false),
                    layer_data = table.Column<string>(type: "text", nullable: true),
                    collision_data = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_village_map_chunks", x => x.id);
                    table.ForeignKey(
                        name: "FK_village_map_chunks_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "village_maps",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    map_type = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    parent_building_id = table.Column<long>(type: "bigint", nullable: true),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    tile_size = table.Column<int>(type: "integer", nullable: false),
                    spawn_x = table.Column<int>(type: "integer", nullable: false),
                    spawn_y = table.Column<int>(type: "integer", nullable: false),
                    tileset_key = table.Column<string>(type: "text", nullable: true),
                    ambient_color = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_village_maps", x => x.id);
                    table.ForeignKey(
                        name: "FK_village_maps_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "village_objects",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    map_id = table.Column<long>(type: "bigint", nullable: false),
                    definition_key = table.Column<string>(type: "text", nullable: true),
                    x = table.Column<int>(type: "integer", nullable: false),
                    y = table.Column<int>(type: "integer", nullable: false),
                    rotation = table.Column<int>(type: "integer", nullable: false),
                    z_index = table.Column<int>(type: "integer", nullable: false),
                    blocks_movement = table.Column<bool>(type: "boolean", nullable: false),
                    owner_member_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_village_objects", x => x.id);
                    table.ForeignKey(
                        name: "FK_village_objects_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "village_plots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    map_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    x = table.Column<int>(type: "integer", nullable: false),
                    y = table.Column<int>(type: "integer", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    owner_member_id = table.Column<long>(type: "bigint", nullable: true),
                    edit_mode = table.Column<int>(type: "integer", nullable: false),
                    for_sale = table.Column<bool>(type: "boolean", nullable: false),
                    price = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_village_plots", x => x.id);
                    table.ForeignKey(
                        name: "FK_village_plots_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_village_buildings_map_id",
                table: "village_buildings",
                column: "map_id");

            migrationBuilder.CreateIndex(
                name: "IX_village_buildings_planet_id",
                table: "village_buildings",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_village_map_chunks_map_id_chunk_x_chunk_y",
                table: "village_map_chunks",
                columns: new[] { "map_id", "chunk_x", "chunk_y" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_village_map_chunks_planet_id",
                table: "village_map_chunks",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_village_maps_planet_id",
                table: "village_maps",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_village_objects_map_id",
                table: "village_objects",
                column: "map_id");

            migrationBuilder.CreateIndex(
                name: "IX_village_objects_planet_id",
                table: "village_objects",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_village_plots_map_id",
                table: "village_plots",
                column: "map_id");

            migrationBuilder.CreateIndex(
                name: "IX_village_plots_planet_id",
                table: "village_plots",
                column: "planet_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "village_buildings");

            migrationBuilder.DropTable(
                name: "village_map_chunks");

            migrationBuilder.DropTable(
                name: "village_maps");

            migrationBuilder.DropTable(
                name: "village_objects");

            migrationBuilder.DropTable(
                name: "village_plots");
        }
    }
}
