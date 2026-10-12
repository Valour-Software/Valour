using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class ShowNsfwPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "show_nsfw",
                table: "user_preferences",
                type: "boolean",
                nullable: false,
                // Existing accounts keep seeing NSFW content. Only accounts
                // registered from the iOS app start with it hidden.
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "show_nsfw",
                table: "user_preferences");
        }
    }
}
