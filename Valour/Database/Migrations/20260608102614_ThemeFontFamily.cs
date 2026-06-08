using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Valour.Database.Context;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ValourDb))]
    [Migration("20260608102614_ThemeFontFamily")]
    public partial class ThemeFontFamily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "font_family",
                table: "themes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "font_family",
                table: "themes");
        }
    }
}
