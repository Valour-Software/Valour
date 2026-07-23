using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class PlanetWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "override_avatar_url",
                table: "messages",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "override_name",
                table: "messages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "webhook_id",
                table: "messages",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "planet_webhooks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    avatar_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    creator_user_id = table.Column<long>(type: "bigint", nullable: false),
                    time_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planet_webhooks", x => x.id);
                    table.ForeignKey(
                        name: "FK_planet_webhooks_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_planet_webhooks_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_planet_webhooks_channel_id",
                table: "planet_webhooks",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_webhooks_planet_id",
                table: "planet_webhooks",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_webhooks_token",
                table: "planet_webhooks",
                column: "token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "planet_webhooks");

            migrationBuilder.DropColumn(
                name: "override_avatar_url",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "override_name",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "webhook_id",
                table: "messages");
        }
    }
}
