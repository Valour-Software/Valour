using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class MemberLastConnectedTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_planet_members_planet_id",
                table: "planet_members");

            migrationBuilder.AddColumn<DateTime>(
                name: "time_last_connected",
                table: "planet_members",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.Sql(
                "UPDATE planet_members AS pm " +
                "SET time_last_connected = u.time_last_active " +
                "FROM users AS u " +
                "WHERE pm.user_id = u.id;");

            migrationBuilder.CreateIndex(
                name: "IX_planet_members_planet_id_time_last_connected",
                table: "planet_members",
                columns: new[] { "planet_id", "time_last_connected" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_planet_members_planet_id_time_last_connected",
                table: "planet_members");

            migrationBuilder.DropColumn(
                name: "time_last_connected",
                table: "planet_members");

            migrationBuilder.CreateIndex(
                name: "IX_planet_members_planet_id",
                table: "planet_members",
                column: "planet_id");
        }
    }
}
