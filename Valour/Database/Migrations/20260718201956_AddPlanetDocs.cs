using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanetDocs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "docs_vanity",
                table: "planets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "enable_docs",
                table: "planets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "public_docs",
                table: "planets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "planet_docs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    parent_id = table.Column<long>(type: "bigint", nullable: true),
                    is_folder = table.Column<bool>(type: "boolean", nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    previous_slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    content = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: true),
                    position = table.Column<long>(type: "bigint", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    time_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_edited = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    last_edited_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true)
                        .Annotation("Npgsql:TsVectorConfig", "simple")
                        .Annotation("Npgsql:TsVectorProperties", new[] { "title", "content" })
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planet_docs", x => x.id);
                    table.ForeignKey(
                        name: "FK_planet_docs_planet_docs_parent_id",
                        column: x => x.parent_id,
                        principalTable: "planet_docs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_planet_docs_planets_planet_id",
                        column: x => x.planet_id,
                        principalTable: "planets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "planet_doc_revisions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    doc_id = table.Column<long>(type: "bigint", nullable: false),
                    planet_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    content = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: true),
                    author_user_id = table.Column<long>(type: "bigint", nullable: false),
                    time_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planet_doc_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_planet_doc_revisions_planet_docs_doc_id",
                        column: x => x.doc_id,
                        principalTable: "planet_docs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_planets_docs_vanity",
                table: "planets",
                column: "docs_vanity",
                unique: true,
                filter: "docs_vanity IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_planet_doc_revisions_doc_id_time_created",
                table: "planet_doc_revisions",
                columns: new[] { "doc_id", "time_created" });

            migrationBuilder.CreateIndex(
                name: "IX_planet_doc_revisions_planet_id",
                table: "planet_doc_revisions",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_docs_parent_id",
                table: "planet_docs",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_docs_planet_id",
                table: "planet_docs",
                column: "planet_id");

            migrationBuilder.CreateIndex(
                name: "IX_planet_docs_planet_id_parent_id_position",
                table: "planet_docs",
                columns: new[] { "planet_id", "parent_id", "position" });

            migrationBuilder.CreateIndex(
                name: "IX_planet_docs_planet_id_previous_slug",
                table: "planet_docs",
                columns: new[] { "planet_id", "previous_slug" },
                filter: "previous_slug IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_planet_docs_planet_id_slug",
                table: "planet_docs",
                columns: new[] { "planet_id", "slug" },
                unique: true,
                filter: "slug IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_planet_docs_search_vector",
                table: "planet_docs",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "planet_doc_revisions");

            migrationBuilder.DropTable(
                name: "planet_docs");

            migrationBuilder.DropIndex(
                name: "IX_planets_docs_vanity",
                table: "planets");

            migrationBuilder.DropColumn(
                name: "docs_vanity",
                table: "planets");

            migrationBuilder.DropColumn(
                name: "enable_docs",
                table: "planets");

            migrationBuilder.DropColumn(
                name: "public_docs",
                table: "planets");
        }
    }
}
