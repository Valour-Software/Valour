using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Valour.Database.Context;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ValourDb))]
    [Migration("20260608142302_ThemeRadiusScale")]
    public partial class ThemeRadiusScale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "radius_xs",
                table: "themes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "radius_sm",
                table: "themes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "radius_md",
                table: "themes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "radius_lg",
                table: "themes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "radius_xl",
                table: "themes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "radius_full",
                table: "themes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "radius_xs",
                table: "themes");

            migrationBuilder.DropColumn(
                name: "radius_sm",
                table: "themes");

            migrationBuilder.DropColumn(
                name: "radius_md",
                table: "themes");

            migrationBuilder.DropColumn(
                name: "radius_lg",
                table: "themes");

            migrationBuilder.DropColumn(
                name: "radius_xl",
                table: "themes");

            migrationBuilder.DropColumn(
                name: "radius_full",
                table: "themes");
        }
    }
}
