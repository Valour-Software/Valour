using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class StaffToolsAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "hide_prior_name",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "pending_mfa_removals",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    target_user_id = table.Column<long>(type: "bigint", nullable: false),
                    staff_user_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    execute_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_mfa_removals", x => x.id);
                    table.ForeignKey(
                        name: "FK_pending_mfa_removals_users_target_user_id",
                        column: x => x.target_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staff_audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    staff_user_id = table.Column<long>(type: "bigint", nullable: false),
                    action_type = table.Column<int>(type: "integer", nullable: false),
                    target_user_id = table.Column<long>(type: "bigint", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    details = table.Column<string>(type: "text", nullable: true),
                    time_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_audit_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pending_mfa_removals_status_execute_at",
                table: "pending_mfa_removals",
                columns: new[] { "status", "execute_at" });

            migrationBuilder.CreateIndex(
                name: "IX_pending_mfa_removals_target_user_id",
                table: "pending_mfa_removals",
                column: "target_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_staff_audit_logs_staff_user_id",
                table: "staff_audit_logs",
                column: "staff_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_staff_audit_logs_target_user_id",
                table: "staff_audit_logs",
                column: "target_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_staff_audit_logs_time_created",
                table: "staff_audit_logs",
                column: "time_created");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_mfa_removals");

            migrationBuilder.DropTable(
                name: "staff_audit_logs");

            migrationBuilder.DropColumn(
                name: "hide_prior_name",
                table: "users");
        }
    }
}
