using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <summary>
    /// Adds the end-to-end encryption tables and the encryption columns on
    /// planets and channels. The columns on messages are added by
    /// MessageEncryptionColumns, and the indexes on large tables by the
    /// migrations after it, so no single step holds locks on several busy
    /// tables at once.
    /// </summary>
    public partial class EndToEndEncryption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Adding columns to planets and channels needs a brief exclusive
            // lock. Give up instead of queueing every query behind a
            // long-running one; the migration runs again on the next start.
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");

            migrationBuilder.AddColumn<int>(
                name: "encryption_mode",
                table: "planets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "encryption_shares_history",
                table: "planets",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "encryption_generation",
                table: "channels",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "e2ee_access_log_entries",
                columns: table => new
                {
                    scope = table.Column<int>(type: "integer", nullable: false),
                    scope_id = table.Column<long>(type: "bigint", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<byte[]>(type: "bytea", nullable: false),
                    signature = table.Column<byte[]>(type: "bytea", nullable: false),
                    signer_user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_checkpoint = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_e2ee_access_log_entries", x => new { x.scope, x.scope_id, x.seq });
                });

            migrationBuilder.CreateTable(
                name: "e2ee_automod_terms",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    trigger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    index_generation = table.Column<int>(type: "integer", nullable: false),
                    terms = table.Column<int[]>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_e2ee_automod_terms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "e2ee_channel_key_boxes",
                columns: table => new
                {
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    generation = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    user_key_generation = table.Column<int>(type: "integer", nullable: false),
                    box = table.Column<byte[]>(type: "bytea", nullable: false),
                    shared_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_e2ee_channel_key_boxes", x => new { x.channel_id, x.generation, x.user_id });
                });

            migrationBuilder.CreateTable(
                name: "e2ee_channel_key_generations",
                columns: table => new
                {
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    generation = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<byte[]>(type: "bytea", nullable: false),
                    signature = table.Column<byte[]>(type: "bytea", nullable: false),
                    creator_user_id = table.Column<long>(type: "bigint", nullable: false),
                    seal_public_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    index_generation = table.Column<int>(type: "integer", nullable: false),
                    held_secret_protected = table.Column<string>(type: "text", nullable: true),
                    has_messages = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_e2ee_channel_key_generations", x => new { x.channel_id, x.generation });
                });

            migrationBuilder.CreateTable(
                name: "e2ee_device_link_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    device_sign_public_key = table.Column<byte[]>(type: "bytea", nullable: true),
                    device_encrypt_public_key = table.Column<byte[]>(type: "bytea", nullable: true),
                    device_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    device_join_proof = table.Column<byte[]>(type: "bytea", nullable: true),
                    join_mac = table.Column<byte[]>(type: "bytea", nullable: true),
                    approved_by_device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_e2ee_device_link_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "e2ee_key_log_entries",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<byte[]>(type: "bytea", nullable: false),
                    signature = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_e2ee_key_log_entries", x => new { x.user_id, x.seq });
                });

            migrationBuilder.CreateTable(
                name: "e2ee_key_requests",
                columns: table => new
                {
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_e2ee_key_requests", x => new { x.channel_id, x.user_id });
                });

            migrationBuilder.CreateTable(
                name: "e2ee_server_keys",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    private_key_protected = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_e2ee_server_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "e2ee_user_key_boxes",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    generation = table.Column<int>(type: "integer", nullable: false),
                    recipient_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    box = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_e2ee_user_key_boxes", x => new { x.user_id, x.generation, x.recipient_id });
                });

            migrationBuilder.CreateTable(
                name: "message_proofs",
                columns: table => new
                {
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: true),
                    author_user_id = table.Column<long>(type: "bigint", nullable: false),
                    encryption_version = table.Column<int>(type: "integer", nullable: false),
                    header = table.Column<byte[]>(type: "bytea", nullable: false),
                    body_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    signature = table.Column<byte[]>(type: "bytea", nullable: true),
                    time_sent = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_proofs", x => new { x.message_id, x.revision });
                });

            migrationBuilder.CreateTable(
                name: "report_evidence",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    report_id = table.Column<string>(type: "text", nullable: true),
                    planet_report_id = table.Column<long>(type: "bigint", nullable: true),
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    author_user_id = table.Column<long>(type: "bigint", nullable: false),
                    time_sent = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    embed = table.Column<string>(type: "text", nullable: true),
                    verification = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_evidence", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_e2ee_automod_terms_channel_id_index_generation",
                table: "e2ee_automod_terms",
                columns: new[] { "channel_id", "index_generation" });

            migrationBuilder.CreateIndex(
                name: "IX_e2ee_automod_terms_trigger_id",
                table: "e2ee_automod_terms",
                column: "trigger_id");

            migrationBuilder.CreateIndex(
                name: "IX_e2ee_channel_key_boxes_user_id_channel_id",
                table: "e2ee_channel_key_boxes",
                columns: new[] { "user_id", "channel_id" });

            migrationBuilder.CreateIndex(
                name: "IX_e2ee_device_link_sessions_user_id",
                table: "e2ee_device_link_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_message_proofs_created_at",
                table: "message_proofs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_report_evidence_planet_report_id",
                table: "report_evidence",
                column: "planet_report_id");

            migrationBuilder.CreateIndex(
                name: "IX_report_evidence_report_id",
                table: "report_evidence",
                column: "report_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "e2ee_access_log_entries");

            migrationBuilder.DropTable(
                name: "e2ee_automod_terms");

            migrationBuilder.DropTable(
                name: "e2ee_channel_key_boxes");

            migrationBuilder.DropTable(
                name: "e2ee_channel_key_generations");

            migrationBuilder.DropTable(
                name: "e2ee_device_link_sessions");

            migrationBuilder.DropTable(
                name: "e2ee_key_log_entries");

            migrationBuilder.DropTable(
                name: "e2ee_key_requests");

            migrationBuilder.DropTable(
                name: "e2ee_server_keys");

            migrationBuilder.DropTable(
                name: "e2ee_user_key_boxes");

            migrationBuilder.DropTable(
                name: "message_proofs");

            migrationBuilder.DropTable(
                name: "report_evidence");

            migrationBuilder.DropColumn(
                name: "encryption_mode",
                table: "planets");

            migrationBuilder.DropColumn(
                name: "encryption_shares_history",
                table: "planets");

            migrationBuilder.DropColumn(
                name: "encryption_generation",
                table: "channels");
        }
    }
}
