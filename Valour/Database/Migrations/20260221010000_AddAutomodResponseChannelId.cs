using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Valour.Database.Context;

#nullable disable

namespace Valour.Database.Migrations;

[DbContext(typeof(ValourDb))]
[Migration("20260221010000_AddAutomodResponseChannelId")]
public partial class AddAutomodResponseChannelId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "response_channel_id",
            table: "automod_actions",
            type: "bigint",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "response_channel_id",
            table: "automod_actions");
    }
}
