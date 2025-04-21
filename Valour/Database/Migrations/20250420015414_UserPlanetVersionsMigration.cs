using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class UserPlanetVersionsMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_planet_role_members_planets_planet_id",
                table: "planet_role_members");

            migrationBuilder.DropForeignKey(
                name: "FK_planet_role_members_users_user_id",
                table: "planet_role_members");

            migrationBuilder.DropIndex(
                name: "IX_planet_role_members_planet_id",
                table: "planet_role_members");

            migrationBuilder.DropIndex(
                name: "IX_planet_role_members_user_id",
                table: "planet_role_members");

            migrationBuilder.DropIndex(
                name: "IX_planet_members_role_hash_key",
                table: "planet_members");

            migrationBuilder.DropIndex(
                name: "IX_planet_members_user_id",
                table: "planet_members");

            migrationBuilder.RenameColumn(
                name: "role_hash_key",
                table: "planet_members",
                newName: "rf3");

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "member_id",
                table: "user_channel_states",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "planets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "local_index",
                table: "planet_roles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "planet_roles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "rf0",
                table: "planet_members",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "rf1",
                table: "planet_members",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "rf2",
                table: "planet_members",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_user_channel_states_member_id",
                table: "user_channel_states",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "IX_referrals_referrer_id",
                table: "referrals",
                column: "referrer_id");

            migrationBuilder.CreateIndex(
                name: "IX_referrals_user_id",
                table: "referrals",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_roles_planet_id_id",
                table: "planet_roles",
                columns: new[] { "planet_id", "id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_referrals_users_referrer_id",
                table: "referrals",
                column: "referrer_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_channel_states_planet_members_member_id",
                table: "user_channel_states",
                column: "member_id",
                principalTable: "planet_members",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_referrals_users_referrer_id",
                table: "referrals");

            migrationBuilder.DropForeignKey(
                name: "FK_user_channel_states_planet_members_member_id",
                table: "user_channel_states");

            migrationBuilder.DropIndex(
                name: "IX_user_channel_states_member_id",
                table: "user_channel_states");

            migrationBuilder.DropIndex(
                name: "IX_referrals_referrer_id",
                table: "referrals");

            migrationBuilder.DropIndex(
                name: "IX_referrals_user_id",
                table: "referrals");

            migrationBuilder.DropIndex(
                name: "IX_planet_roles_planet_id_id",
                table: "planet_roles");

            migrationBuilder.DropColumn(
                name: "version",
                table: "users");

            migrationBuilder.DropColumn(
                name: "member_id",
                table: "user_channel_states");

            migrationBuilder.DropColumn(
                name: "version",
                table: "planets");

            migrationBuilder.DropColumn(
                name: "local_index",
                table: "planet_roles");

            migrationBuilder.DropColumn(
                name: "version",
                table: "planet_roles");

            migrationBuilder.DropColumn(
                name: "rf0",
                table: "planet_members");

            migrationBuilder.DropColumn(
                name: "rf1",
                table: "planet_members");

            migrationBuilder.DropColumn(
                name: "rf2",
                table: "planet_members");

            migrationBuilder.RenameColumn(
                name: "rf3",
                table: "planet_members",
                newName: "role_hash_key");

            migrationBuilder.CreateIndex(
                name: "IX_planet_role_members_planet_id",
                table: "planet_role_members",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_role_members_user_id",
                table: "planet_role_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_members_role_hash_key",
                table: "planet_members",
                column: "role_hash_key");

            migrationBuilder.CreateIndex(
                name: "IX_planet_members_user_id",
                table: "planet_members",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_planet_role_members_planets_planet_id",
                table: "planet_role_members",
                column: "planet_id",
                principalTable: "planets",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_planet_role_members_users_user_id",
                table: "planet_role_members",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
