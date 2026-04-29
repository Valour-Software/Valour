using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class MessageAttachmentsMentionsAndMediaSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attachments_data",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "embed_data",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "mentions_data",
                table: "messages");

            migrationBuilder.AddColumn<string>(
                name: "safety_details",
                table: "cdn_bucket_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "safety_match_id",
                table: "cdn_bucket_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "safety_provider",
                table: "cdn_bucket_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "safety_quarantined_at",
                table: "cdn_bucket_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "safety_hash_match_state",
                table: "cdn_bucket_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "safety_hash_matched_at",
                table: "cdn_bucket_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sha256_hash",
                table: "cdn_bucket_items",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "message_attachments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<long>(type: "bigint", nullable: false),
                    cdn_bucket_item_id = table.Column<string>(type: "text", nullable: true),
                    location = table.Column<string>(type: "text", nullable: false),
                    mime_type = table.Column<string>(type: "text", nullable: true),
                    file_name = table.Column<string>(type: "text", nullable: true),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    inline = table.Column<bool>(type: "boolean", nullable: false),
                    missing = table.Column<bool>(type: "boolean", nullable: false),
                    data = table.Column<string>(type: "text", nullable: true),
                    open_graph_data = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_attachments", x => x.id);
                    table.ForeignKey(
                        name: "FK_message_attachments_cdn_bucket_items_cdn_bucket_item_id",
                        column: x => x.cdn_bucket_item_id,
                        principalTable: "cdn_bucket_items",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_message_attachments_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "message_mentions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    target_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_mentions", x => x.id);
                    table.ForeignKey(
                        name: "FK_message_mentions_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cdn_bucket_items_hash",
                table: "cdn_bucket_items",
                column: "hash");

            migrationBuilder.CreateIndex(
                name: "IX_cdn_bucket_items_safety_hash_match_state",
                table: "cdn_bucket_items",
                column: "safety_hash_match_state");

            migrationBuilder.CreateIndex(
                name: "IX_cdn_bucket_items_sha256_hash",
                table: "cdn_bucket_items",
                column: "sha256_hash");

            migrationBuilder.CreateIndex(
                name: "IX_cdn_bucket_items_user_id",
                table: "cdn_bucket_items",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_message_attachments_cdn_bucket_item_id",
                table: "message_attachments",
                column: "cdn_bucket_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_message_attachments_message_id",
                table: "message_attachments",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "IX_message_mentions_message_id",
                table: "message_mentions",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "IX_message_mentions_type_target_id",
                table: "message_mentions",
                columns: new[] { "type", "target_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_attachments");

            migrationBuilder.DropTable(
                name: "message_mentions");

            migrationBuilder.DropIndex(
                name: "IX_cdn_bucket_items_hash",
                table: "cdn_bucket_items");

            migrationBuilder.DropIndex(
                name: "IX_cdn_bucket_items_safety_hash_match_state",
                table: "cdn_bucket_items");

            migrationBuilder.DropIndex(
                name: "IX_cdn_bucket_items_sha256_hash",
                table: "cdn_bucket_items");

            migrationBuilder.DropIndex(
                name: "IX_cdn_bucket_items_user_id",
                table: "cdn_bucket_items");

            migrationBuilder.DropColumn(
                name: "safety_details",
                table: "cdn_bucket_items");

            migrationBuilder.DropColumn(
                name: "safety_match_id",
                table: "cdn_bucket_items");

            migrationBuilder.DropColumn(
                name: "safety_provider",
                table: "cdn_bucket_items");

            migrationBuilder.DropColumn(
                name: "safety_quarantined_at",
                table: "cdn_bucket_items");

            migrationBuilder.DropColumn(
                name: "safety_hash_match_state",
                table: "cdn_bucket_items");

            migrationBuilder.DropColumn(
                name: "safety_hash_matched_at",
                table: "cdn_bucket_items");

            migrationBuilder.DropColumn(
                name: "sha256_hash",
                table: "cdn_bucket_items");

            migrationBuilder.AddColumn<string>(
                name: "attachments_data",
                table: "messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "embed_data",
                table: "messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mentions_data",
                table: "messages",
                type: "text",
                nullable: true);
        }
    }
}
