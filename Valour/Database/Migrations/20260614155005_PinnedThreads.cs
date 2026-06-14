using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class PinnedThreads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_planet_threads_planet_id_is_pinned",
                table: "planet_threads");

            migrationBuilder.DropColumn(
                name: "is_pinned",
                table: "planet_threads");

            migrationBuilder.AddColumn<long>(
                name: "pinned_thread_id",
                table: "planets",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "dismissed_pin_thread_id",
                table: "planet_members",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pinned_thread_id",
                table: "planets");

            migrationBuilder.DropColumn(
                name: "dismissed_pin_thread_id",
                table: "planet_members");

            migrationBuilder.AddColumn<bool>(
                name: "is_pinned",
                table: "planet_threads",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_planet_threads_planet_id_is_pinned",
                table: "planet_threads",
                columns: new[] { "planet_id", "is_pinned" });
        }
    }
}
