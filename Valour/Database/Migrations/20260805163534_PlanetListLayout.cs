using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class PlanetListLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "folder_id",
                table: "user_planet_settings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "position",
                table: "user_planet_settings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_planet_folders",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_planet_folders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_planet_folders_user_id_position",
                table: "user_planet_folders",
                columns: new[] { "user_id", "position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_planet_folders");

            migrationBuilder.DropColumn(
                name: "folder_id",
                table: "user_planet_settings");

            migrationBuilder.DropColumn(
                name: "position",
                table: "user_planet_settings");
        }
    }
}
