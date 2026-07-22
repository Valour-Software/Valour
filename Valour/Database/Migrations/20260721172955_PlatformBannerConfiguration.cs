using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class PlatformBannerConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_banner_configuration",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    updated_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_banner_configuration", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_banner_configuration");
        }
    }
}
