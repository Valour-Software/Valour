using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valour.Database.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Hand-written as pure renames (EF scaffolds drop/create when the CLR
    /// types change, which would lose data). Renames the docs feature to wiki:
    /// tables, columns, indexes, and constraints — plus the planet vanity
    /// column, which is promoted from a docs setting to a planet-level name.
    /// </remarks>
    public partial class RenameDocsToWiki : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tables
            migrationBuilder.RenameTable(name: "planet_docs", newName: "planet_wiki_pages");
            migrationBuilder.RenameTable(name: "planet_doc_revisions", newName: "planet_wiki_revisions");

            // Columns
            migrationBuilder.RenameColumn(name: "doc_id", table: "planet_wiki_revisions", newName: "page_id");
            migrationBuilder.RenameColumn(name: "enable_docs", table: "planets", newName: "enable_wiki");
            migrationBuilder.RenameColumn(name: "public_docs", table: "planets", newName: "public_wiki");
            migrationBuilder.RenameColumn(name: "docs_vanity", table: "planets", newName: "vanity");

            // Indexes
            migrationBuilder.RenameIndex(name: "IX_planet_docs_parent_id", table: "planet_wiki_pages", newName: "IX_planet_wiki_pages_parent_id");
            migrationBuilder.RenameIndex(name: "IX_planet_docs_planet_id", table: "planet_wiki_pages", newName: "IX_planet_wiki_pages_planet_id");
            migrationBuilder.RenameIndex(name: "IX_planet_docs_planet_id_parent_id_position", table: "planet_wiki_pages", newName: "IX_planet_wiki_pages_planet_id_parent_id_position");
            migrationBuilder.RenameIndex(name: "IX_planet_docs_planet_id_previous_slug", table: "planet_wiki_pages", newName: "IX_planet_wiki_pages_planet_id_previous_slug");
            migrationBuilder.RenameIndex(name: "IX_planet_docs_planet_id_slug", table: "planet_wiki_pages", newName: "IX_planet_wiki_pages_planet_id_slug");
            migrationBuilder.RenameIndex(name: "IX_planet_docs_search_vector", table: "planet_wiki_pages", newName: "IX_planet_wiki_pages_search_vector");
            migrationBuilder.RenameIndex(name: "IX_planet_doc_revisions_doc_id_time_created", table: "planet_wiki_revisions", newName: "IX_planet_wiki_revisions_page_id_time_created");
            migrationBuilder.RenameIndex(name: "IX_planet_doc_revisions_planet_id", table: "planet_wiki_revisions", newName: "IX_planet_wiki_revisions_planet_id");
            migrationBuilder.RenameIndex(name: "IX_planets_docs_vanity", table: "planets", newName: "IX_planets_vanity");

            // Primary/foreign key constraint names (no fluent API for these)
            migrationBuilder.Sql("ALTER TABLE planet_wiki_pages RENAME CONSTRAINT \"PK_planet_docs\" TO \"PK_planet_wiki_pages\";");
            migrationBuilder.Sql("ALTER TABLE planet_wiki_pages RENAME CONSTRAINT \"FK_planet_docs_planets_planet_id\" TO \"FK_planet_wiki_pages_planets_planet_id\";");
            migrationBuilder.Sql("ALTER TABLE planet_wiki_pages RENAME CONSTRAINT \"FK_planet_docs_planet_docs_parent_id\" TO \"FK_planet_wiki_pages_planet_wiki_pages_parent_id\";");
            migrationBuilder.Sql("ALTER TABLE planet_wiki_revisions RENAME CONSTRAINT \"PK_planet_doc_revisions\" TO \"PK_planet_wiki_revisions\";");
            migrationBuilder.Sql("ALTER TABLE planet_wiki_revisions RENAME CONSTRAINT \"FK_planet_doc_revisions_planet_docs_doc_id\" TO \"FK_planet_wiki_revisions_planet_wiki_pages_page_id\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE planet_wiki_revisions RENAME CONSTRAINT \"FK_planet_wiki_revisions_planet_wiki_pages_page_id\" TO \"FK_planet_doc_revisions_planet_docs_doc_id\";");
            migrationBuilder.Sql("ALTER TABLE planet_wiki_revisions RENAME CONSTRAINT \"PK_planet_wiki_revisions\" TO \"PK_planet_doc_revisions\";");
            migrationBuilder.Sql("ALTER TABLE planet_wiki_pages RENAME CONSTRAINT \"FK_planet_wiki_pages_planet_wiki_pages_parent_id\" TO \"FK_planet_docs_planet_docs_parent_id\";");
            migrationBuilder.Sql("ALTER TABLE planet_wiki_pages RENAME CONSTRAINT \"FK_planet_wiki_pages_planets_planet_id\" TO \"FK_planet_docs_planets_planet_id\";");
            migrationBuilder.Sql("ALTER TABLE planet_wiki_pages RENAME CONSTRAINT \"PK_planet_wiki_pages\" TO \"PK_planet_docs\";");

            migrationBuilder.RenameIndex(name: "IX_planets_vanity", table: "planets", newName: "IX_planets_docs_vanity");
            migrationBuilder.RenameIndex(name: "IX_planet_wiki_revisions_planet_id", table: "planet_wiki_revisions", newName: "IX_planet_doc_revisions_planet_id");
            migrationBuilder.RenameIndex(name: "IX_planet_wiki_revisions_page_id_time_created", table: "planet_wiki_revisions", newName: "IX_planet_doc_revisions_doc_id_time_created");
            migrationBuilder.RenameIndex(name: "IX_planet_wiki_pages_search_vector", table: "planet_wiki_pages", newName: "IX_planet_docs_search_vector");
            migrationBuilder.RenameIndex(name: "IX_planet_wiki_pages_planet_id_slug", table: "planet_wiki_pages", newName: "IX_planet_docs_planet_id_slug");
            migrationBuilder.RenameIndex(name: "IX_planet_wiki_pages_planet_id_previous_slug", table: "planet_wiki_pages", newName: "IX_planet_docs_planet_id_previous_slug");
            migrationBuilder.RenameIndex(name: "IX_planet_wiki_pages_planet_id_parent_id_position", table: "planet_wiki_pages", newName: "IX_planet_docs_planet_id_parent_id_position");
            migrationBuilder.RenameIndex(name: "IX_planet_wiki_pages_planet_id", table: "planet_wiki_pages", newName: "IX_planet_docs_planet_id");
            migrationBuilder.RenameIndex(name: "IX_planet_wiki_pages_parent_id", table: "planet_wiki_pages", newName: "IX_planet_docs_parent_id");

            migrationBuilder.RenameColumn(name: "vanity", table: "planets", newName: "docs_vanity");
            migrationBuilder.RenameColumn(name: "public_wiki", table: "planets", newName: "public_docs");
            migrationBuilder.RenameColumn(name: "enable_wiki", table: "planets", newName: "enable_docs");
            migrationBuilder.RenameColumn(name: "page_id", table: "planet_wiki_revisions", newName: "doc_id");

            migrationBuilder.RenameTable(name: "planet_wiki_revisions", newName: "planet_doc_revisions");
            migrationBuilder.RenameTable(name: "planet_wiki_pages", newName: "planet_docs");
        }
    }
}
