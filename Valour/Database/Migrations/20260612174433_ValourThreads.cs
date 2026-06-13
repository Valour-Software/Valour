using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class ValourThreads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "thread_comment_id",
                table: "reports",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "thread_id",
                table: "reports",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "enable_threads",
                table: "planets",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "public_threads",
                table: "planets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "thread_comment_id",
                table: "planet_reports",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "thread_id",
                table: "planet_reports",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "planet_threads",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    author_user_id = table.Column<long>(type: "bigint", nullable: false),
                    author_member_id = table.Column<long>(type: "bigint", nullable: true),
                    title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    content = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    time_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    edited_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false),
                    nsfw = table.Column<bool>(type: "boolean", nullable: false),
                    boost_count = table.Column<int>(type: "integer", nullable: false),
                    comment_count = table.Column<int>(type: "integer", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planet_threads", x => x.id);
                    table.ForeignKey(
                        name: "FK_planet_threads_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "thread_attachments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    thread_id = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_thread_attachments", x => x.id);
                    table.ForeignKey(
                        name: "FK_thread_attachments_cdn_bucket_items_cdn_bucket_item_id",
                        column: x => x.cdn_bucket_item_id,
                        principalTable: "cdn_bucket_items",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_thread_attachments_planet_threads_thread_id",
                        column: x => x.thread_id,
                        principalTable: "planet_threads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "thread_boosts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    thread_id = table.Column<long>(type: "bigint", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thread_boosts", x => x.id);
                    table.ForeignKey(
                        name: "FK_thread_boosts_planet_threads_thread_id",
                        column: x => x.thread_id,
                        principalTable: "planet_threads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "thread_comments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    thread_id = table.Column<long>(type: "bigint", nullable: false),
                    parent_comment_id = table.Column<long>(type: "bigint", nullable: true),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    author_user_id = table.Column<long>(type: "bigint", nullable: false),
                    author_member_id = table.Column<long>(type: "bigint", nullable: true),
                    content = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    time_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    edited_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    boost_count = table.Column<int>(type: "integer", nullable: false),
                    reply_count = table.Column<int>(type: "integer", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thread_comments", x => x.id);
                    table.ForeignKey(
                        name: "FK_thread_comments_planet_threads_thread_id",
                        column: x => x.thread_id,
                        principalTable: "planet_threads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_thread_comments_thread_comments_parent_comment_id",
                        column: x => x.parent_comment_id,
                        principalTable: "thread_comments",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "thread_comment_boosts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    comment_id = table.Column<long>(type: "bigint", nullable: false),
                    thread_id = table.Column<long>(type: "bigint", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thread_comment_boosts", x => x.id);
                    table.ForeignKey(
                        name: "FK_thread_comment_boosts_thread_comments_comment_id",
                        column: x => x.comment_id,
                        principalTable: "thread_comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reports_thread_comment_id",
                table: "reports",
                column: "thread_comment_id");

            migrationBuilder.CreateIndex(
                name: "IX_reports_thread_id",
                table: "reports",
                column: "thread_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_reports_thread_comment_id",
                table: "planet_reports",
                column: "thread_comment_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_reports_thread_id",
                table: "planet_reports",
                column: "thread_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_threads_author_user_id",
                table: "planet_threads",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_threads_planet_id",
                table: "planet_threads",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_threads_planet_id_is_pinned",
                table: "planet_threads",
                columns: new[] { "planet_id", "is_pinned" });

            migrationBuilder.CreateIndex(
                name: "IX_planet_threads_planet_id_time_created",
                table: "planet_threads",
                columns: new[] { "planet_id", "time_created" });

            migrationBuilder.CreateIndex(
                name: "IX_thread_attachments_cdn_bucket_item_id",
                table: "thread_attachments",
                column: "cdn_bucket_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_thread_attachments_thread_id",
                table: "thread_attachments",
                column: "thread_id");

            migrationBuilder.CreateIndex(
                name: "IX_thread_boosts_thread_id_user_id",
                table: "thread_boosts",
                columns: new[] { "thread_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_thread_boosts_user_id",
                table: "thread_boosts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_thread_comment_boosts_comment_id_user_id",
                table: "thread_comment_boosts",
                columns: new[] { "comment_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_thread_comment_boosts_thread_id",
                table: "thread_comment_boosts",
                column: "thread_id");

            migrationBuilder.CreateIndex(
                name: "IX_thread_comment_boosts_user_id",
                table: "thread_comment_boosts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_thread_comments_author_user_id",
                table: "thread_comments",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_thread_comments_parent_comment_id",
                table: "thread_comments",
                column: "parent_comment_id");

            migrationBuilder.CreateIndex(
                name: "IX_thread_comments_planet_id",
                table: "thread_comments",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_thread_comments_thread_id",
                table: "thread_comments",
                column: "thread_id");

            migrationBuilder.CreateIndex(
                name: "IX_thread_comments_thread_id_parent_comment_id",
                table: "thread_comments",
                columns: new[] { "thread_id", "parent_comment_id" });

            // Public thread browsing defaults on for discoverable planets
            migrationBuilder.Sql("UPDATE planets SET public_threads = discoverable;");

            // Grant PostThreads (0x10000) and CommentOnThreads (0x20000) to existing default roles
            migrationBuilder.Sql("UPDATE planet_roles SET permissions = permissions | 196608 WHERE is_default = true;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "thread_attachments");

            migrationBuilder.DropTable(
                name: "thread_boosts");

            migrationBuilder.DropTable(
                name: "thread_comment_boosts");

            migrationBuilder.DropTable(
                name: "thread_comments");

            migrationBuilder.DropTable(
                name: "planet_threads");

            migrationBuilder.DropIndex(
                name: "IX_reports_thread_comment_id",
                table: "reports");

            migrationBuilder.DropIndex(
                name: "IX_reports_thread_id",
                table: "reports");

            migrationBuilder.DropIndex(
                name: "IX_planet_reports_thread_comment_id",
                table: "planet_reports");

            migrationBuilder.DropIndex(
                name: "IX_planet_reports_thread_id",
                table: "planet_reports");

            migrationBuilder.DropColumn(
                name: "thread_comment_id",
                table: "reports");

            migrationBuilder.DropColumn(
                name: "thread_id",
                table: "reports");

            migrationBuilder.DropColumn(
                name: "enable_threads",
                table: "planets");

            migrationBuilder.DropColumn(
                name: "public_threads",
                table: "planets");

            migrationBuilder.DropColumn(
                name: "thread_comment_id",
                table: "planet_reports");

            migrationBuilder.DropColumn(
                name: "thread_id",
                table: "planet_reports");
        }
    }
}
