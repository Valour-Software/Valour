using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class Conversions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_auth_tokens_user_id",
                table: "auth_tokens");

            migrationBuilder.RenameColumn(
                name: "code",
                table: "planet_invites",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "redirect_url",
                table: "oauth_apps",
                newName: "RedirectUrl");

            migrationBuilder.CreateIndex(
                name: "IX_reports_channel_id",
                table: "reports",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "IX_reports_message_id",
                table: "reports",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "IX_reports_planet_id",
                table: "reports",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_reports_reporting_user_id",
                table: "reports",
                column: "reporting_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_invites_issuer_id",
                table: "planet_invites",
                column: "issuer_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_planet_invites_time_created_time_expires",
                table: "planet_invites",
                columns: new[] { "time_created", "time_expires" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_tokens_id",
                table: "auth_tokens",
                column: "id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_auth_tokens_scope",
                table: "auth_tokens",
                column: "scope");

            migrationBuilder.CreateIndex(
                name: "IX_auth_tokens_user_id",
                table: "auth_tokens",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_reports_channel_id",
                table: "reports");

            migrationBuilder.DropIndex(
                name: "IX_reports_message_id",
                table: "reports");

            migrationBuilder.DropIndex(
                name: "IX_reports_planet_id",
                table: "reports");

            migrationBuilder.DropIndex(
                name: "IX_reports_reporting_user_id",
                table: "reports");

            migrationBuilder.DropIndex(
                name: "IX_planet_invites_issuer_id",
                table: "planet_invites");

            migrationBuilder.DropIndex(
                name: "IX_planet_invites_time_created_time_expires",
                table: "planet_invites");

            migrationBuilder.DropIndex(
                name: "IX_auth_tokens_id",
                table: "auth_tokens");

            migrationBuilder.DropIndex(
                name: "IX_auth_tokens_scope",
                table: "auth_tokens");

            migrationBuilder.DropIndex(
                name: "IX_auth_tokens_user_id",
                table: "auth_tokens");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "planet_invites",
                newName: "code");

            migrationBuilder.RenameColumn(
                name: "RedirectUrl",
                table: "oauth_apps",
                newName: "redirect_url");

            migrationBuilder.CreateIndex(
                name: "IX_auth_tokens_user_id",
                table: "auth_tokens",
                column: "user_id");
        }
    }
}
