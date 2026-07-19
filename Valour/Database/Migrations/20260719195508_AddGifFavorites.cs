using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Valour.Database.Context;

#nullable disable

namespace Valour.Database.Migrations;

[DbContext(typeof(ValourDb))]
[Migration("20260719195508_AddGifFavorites")]
public partial class AddGifFavorites : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "gif_favorites",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<long>(type: "bigint", nullable: false),
                provider = table.Column<string>(type: "text", nullable: false),
                provider_id = table.Column<string>(type: "text", nullable: false),
                title = table.Column<string>(type: "text", nullable: false),
                preview_url = table.Column<string>(type: "text", nullable: false),
                gif_url = table.Column<string>(type: "text", nullable: false),
                width = table.Column<int>(type: "integer", nullable: false),
                height = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gif_favorites", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_gif_favorites_user_id_provider_provider_id",
            table: "gif_favorites",
            columns: new[] { "user_id", "provider", "provider_id" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "gif_favorites");
    }
}
