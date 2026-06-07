using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class PlanetReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "planet_reports",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    time_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reporting_user_id = table.Column<long>(type: "bigint", nullable: false),
                    reported_user_id = table.Column<long>(type: "bigint", nullable: true),
                    reported_member_id = table.Column<long>(type: "bigint", nullable: true),
                    message_id = table.Column<long>(type: "bigint", nullable: true),
                    channel_id = table.Column<long>(type: "bigint", nullable: true),
                    rule_id = table.Column<long>(type: "bigint", nullable: true),
                    rule_title_snapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rule_description_snapshot = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    long_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    reviewed = table.Column<bool>(type: "boolean", nullable: false),
                    resolution = table.Column<int>(type: "integer", nullable: false),
                    resolved_by_id = table.Column<long>(type: "bigint", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    moderator_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planet_reports", x => x.id);
                    table.ForeignKey(
                        name: "FK_planet_reports_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_planet_reports_planet_id",
                table: "planet_reports",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_reports_reported_member_id",
                table: "planet_reports",
                column: "reported_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_reports_reported_user_id",
                table: "planet_reports",
                column: "reported_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_reports_reporting_user_id",
                table: "planet_reports",
                column: "reporting_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_reports_resolution",
                table: "planet_reports",
                column: "resolution");

            migrationBuilder.CreateIndex(
                name: "IX_planet_reports_resolved_by_id",
                table: "planet_reports",
                column: "resolved_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_reports_rule_id",
                table: "planet_reports",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_reports_time_created",
                table: "planet_reports",
                column: "time_created");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "planet_reports");
        }
    }
}
