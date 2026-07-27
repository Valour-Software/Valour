using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class BroaderInterestTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "tags",
                columns: new[] { "id", "created_date", "name", "slug" },
                values: new object[,]
                {
                    { 11L, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Music", "music" },
                    { 12L, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Art", "art" },
                    { 13L, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Technology", "technology" },
                    { 14L, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Programming", "programming" },
                    { 15L, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Science", "science" },
                    { 16L, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Movies & TV", "movies-tv" },
                    { 17L, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Books & Writing", "books-writing" },
                    { 18L, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Memes", "memes" },
                    { 19L, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Sports", "sports" },
                    { 20L, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Fitness", "fitness" },
                    { 21L, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Food & Cooking", "food-cooking" },
                    { 22L, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Roleplay", "roleplay" },
                    { 23L, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Pets & Animals", "pets-animals" },
                    { 24L, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Education", "education" },
                    { 25L, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Travel", "travel" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "tags",
                keyColumn: "id",
                keyValue: 11L);

            migrationBuilder.DeleteData(
                table: "tags",
                keyColumn: "id",
                keyValue: 12L);

            migrationBuilder.DeleteData(
                table: "tags",
                keyColumn: "id",
                keyValue: 13L);

            migrationBuilder.DeleteData(
                table: "tags",
                keyColumn: "id",
                keyValue: 14L);

            migrationBuilder.DeleteData(
                table: "tags",
                keyColumn: "id",
                keyValue: 15L);

            migrationBuilder.DeleteData(
                table: "tags",
                keyColumn: "id",
                keyValue: 16L);

            migrationBuilder.DeleteData(
                table: "tags",
                keyColumn: "id",
                keyValue: 17L);

            migrationBuilder.DeleteData(
                table: "tags",
                keyColumn: "id",
                keyValue: 18L);

            migrationBuilder.DeleteData(
                table: "tags",
                keyColumn: "id",
                keyValue: 19L);

            migrationBuilder.DeleteData(
                table: "tags",
                keyColumn: "id",
                keyValue: 20L);

            migrationBuilder.DeleteData(
                table: "tags",
                keyColumn: "id",
                keyValue: 21L);

            migrationBuilder.DeleteData(
                table: "tags",
                keyColumn: "id",
                keyValue: 22L);

            migrationBuilder.DeleteData(
                table: "tags",
                keyColumn: "id",
                keyValue: 23L);

            migrationBuilder.DeleteData(
                table: "tags",
                keyColumn: "id",
                keyValue: 24L);

            migrationBuilder.DeleteData(
                table: "tags",
                keyColumn: "id",
                keyValue: 25L);
        }
    }
}
