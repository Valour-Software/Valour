using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunityNodeIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "identity_authority",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "identity_authority_user_id",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_shadow_user",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "audience",
                table: "auth_tokens",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "token_type",
                table: "auth_tokens",
                type: "text",
                nullable: false,
                defaultValue: "official");

            migrationBuilder.CreateIndex(
                name: "IX_users_identity_authority_identity_authority_user_id",
                table: "users",
                columns: new[] { "identity_authority", "identity_authority_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_tokens_token_type",
                table: "auth_tokens",
                column: "token_type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_identity_authority_identity_authority_user_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_auth_tokens_token_type",
                table: "auth_tokens");

            migrationBuilder.DropColumn(
                name: "identity_authority",
                table: "users");

            migrationBuilder.DropColumn(
                name: "identity_authority_user_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_shadow_user",
                table: "users");

            migrationBuilder.DropColumn(
                name: "audience",
                table: "auth_tokens");

            migrationBuilder.DropColumn(
                name: "token_type",
                table: "auth_tokens");
        }
    }
}
