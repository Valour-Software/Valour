using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class DirectGroupCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "call_policy",
                table: "user_preferences",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "is_admin",
                table: "channel_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "direct_calls",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    caller_user_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    end_reason = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_direct_calls", x => x.id);
                    table.ForeignKey(
                        name: "FK_direct_calls_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_direct_calls_users_caller_user_id",
                        column: x => x.caller_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "direct_call_members",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    call_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    is_caller = table.Column<bool>(type: "boolean", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    responded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_direct_call_members", x => x.id);
                    table.ForeignKey(
                        name: "FK_direct_call_members_direct_calls_call_id",
                        column: x => x.call_id,
                        principalTable: "direct_calls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_direct_call_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_direct_call_members_call_id_user_id",
                table: "direct_call_members",
                columns: new[] { "call_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_direct_call_members_user_id",
                table: "direct_call_members",
                column: "user_id",
                unique: true,
                filter: "state IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_direct_calls_caller_user_id",
                table: "direct_calls",
                column: "caller_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_direct_calls_channel_id",
                table: "direct_calls",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "IX_direct_calls_state_expires_at",
                table: "direct_calls",
                columns: new[] { "state", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "direct_call_members");

            migrationBuilder.DropTable(
                name: "direct_calls");

            migrationBuilder.DropColumn(
                name: "call_policy",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "is_admin",
                table: "channel_members");
        }
    }
}
