using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class ChannelConversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_channel_members_channels_channel_id",
                table: "channel_members");

            migrationBuilder.DropForeignKey(
                name: "FK_channels_channels_parent_id",
                table: "channels");

            migrationBuilder.DropForeignKey(
                name: "FK_channels_planets_planet_id",
                table: "channels");

            migrationBuilder.DropForeignKey(
                name: "FK_messages_channels_channel_id",
                table: "messages");

            migrationBuilder.DropForeignKey(
                name: "FK_permissions_nodes_channels_target_id",
                table: "permissions_nodes");

            migrationBuilder.DropForeignKey(
                name: "FK_user_channel_states_channels_channel_id",
                table: "user_channel_states");

            migrationBuilder.DropPrimaryKey(
                name: "PK_channels",
                table: "channels");

            migrationBuilder.RenameTable(
                name: "channels",
                newName: "Channels");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Channels",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "Channels",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Channels",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "position",
                table: "Channels",
                newName: "RawPosition");

            migrationBuilder.RenameColumn(
                name: "planet_id",
                table: "Channels",
                newName: "PlanetId");

            migrationBuilder.RenameColumn(
                name: "parent_id",
                table: "Channels",
                newName: "ParentId");

            migrationBuilder.RenameColumn(
                name: "last_update_time",
                table: "Channels",
                newName: "LastUpdateTime");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "Channels",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "is_default",
                table: "Channels",
                newName: "IsDefault");

            migrationBuilder.RenameColumn(
                name: "inherits_perms",
                table: "Channels",
                newName: "InheritsPerms");

            migrationBuilder.RenameColumn(
                name: "channel_type",
                table: "Channels",
                newName: "ChannelType");

            migrationBuilder.RenameIndex(
                name: "IX_channels_planet_id",
                table: "Channels",
                newName: "IX_Channels_PlanetId");

            migrationBuilder.RenameIndex(
                name: "IX_channels_parent_id",
                table: "Channels",
                newName: "IX_Channels_ParentId");

            migrationBuilder.AddColumn<byte>(
                name: "Version",
                table: "Channels",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Channels",
                table: "Channels",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_channel_members_Channels_channel_id",
                table: "channel_members",
                column: "channel_id",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Channels_Channels_ParentId",
                table: "Channels",
                column: "ParentId",
                principalTable: "Channels",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Channels_planets_PlanetId",
                table: "Channels",
                column: "PlanetId",
                principalTable: "planets",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_messages_Channels_channel_id",
                table: "messages",
                column: "channel_id",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_permissions_nodes_Channels_target_id",
                table: "permissions_nodes",
                column: "target_id",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_channel_states_Channels_channel_id",
                table: "user_channel_states",
                column: "channel_id",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_channel_members_Channels_channel_id",
                table: "channel_members");

            migrationBuilder.DropForeignKey(
                name: "FK_Channels_Channels_ParentId",
                table: "Channels");

            migrationBuilder.DropForeignKey(
                name: "FK_Channels_planets_PlanetId",
                table: "Channels");

            migrationBuilder.DropForeignKey(
                name: "FK_messages_Channels_channel_id",
                table: "messages");

            migrationBuilder.DropForeignKey(
                name: "FK_permissions_nodes_Channels_target_id",
                table: "permissions_nodes");

            migrationBuilder.DropForeignKey(
                name: "FK_user_channel_states_Channels_channel_id",
                table: "user_channel_states");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Channels",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Channels");

            migrationBuilder.RenameTable(
                name: "Channels",
                newName: "channels");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "channels",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "channels",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "channels",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "RawPosition",
                table: "channels",
                newName: "position");

            migrationBuilder.RenameColumn(
                name: "PlanetId",
                table: "channels",
                newName: "planet_id");

            migrationBuilder.RenameColumn(
                name: "ParentId",
                table: "channels",
                newName: "parent_id");

            migrationBuilder.RenameColumn(
                name: "LastUpdateTime",
                table: "channels",
                newName: "last_update_time");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "channels",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "IsDefault",
                table: "channels",
                newName: "is_default");

            migrationBuilder.RenameColumn(
                name: "InheritsPerms",
                table: "channels",
                newName: "inherits_perms");

            migrationBuilder.RenameColumn(
                name: "ChannelType",
                table: "channels",
                newName: "channel_type");

            migrationBuilder.RenameIndex(
                name: "IX_Channels_PlanetId",
                table: "channels",
                newName: "IX_channels_planet_id");

            migrationBuilder.RenameIndex(
                name: "IX_Channels_ParentId",
                table: "channels",
                newName: "IX_channels_parent_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_channels",
                table: "channels",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_channel_members_channels_channel_id",
                table: "channel_members",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_channels_channels_parent_id",
                table: "channels",
                column: "parent_id",
                principalTable: "channels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_channels_planets_planet_id",
                table: "channels",
                column: "planet_id",
                principalTable: "planets",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_messages_channels_channel_id",
                table: "messages",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_permissions_nodes_channels_target_id",
                table: "permissions_nodes",
                column: "target_id",
                principalTable: "channels",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_channel_states_channels_channel_id",
                table: "user_channel_states",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
