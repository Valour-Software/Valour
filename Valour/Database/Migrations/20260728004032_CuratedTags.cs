using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class CuratedTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "curated",
                table: "tags",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 1L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 2L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 3L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 4L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 5L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 6L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 7L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 8L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 9L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 10L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 11L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 12L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 13L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 14L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 15L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 16L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 17L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 18L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 19L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 20L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 21L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 22L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 23L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 24L,
                column: "curated",
                value: true);

            migrationBuilder.UpdateData(
                table: "tags",
                keyColumn: "id",
                keyValue: 25L,
                column: "curated",
                value: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "curated",
                table: "tags");
        }
    }
}
