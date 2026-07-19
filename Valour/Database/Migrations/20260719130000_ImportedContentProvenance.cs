using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Valour.Database.Context;

#nullable disable

namespace Valour.Database.Migrations;

[DbContext(typeof(ValourDb))]
[Migration("20260719130000_ImportedContentProvenance")]
/// <summary>
/// Marks content introduced by a trusted import workflow without changing its
/// original author, timestamp, or moderation state.  A nullable source keeps
/// every pre-existing/native record unmarked.
/// </summary>
public partial class ImportedContentProvenance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "import_source",
            table: "messages",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "import_source",
            table: "message_reactions",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "import_source",
            table: "planet_threads",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "import_source",
            table: "thread_comments",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "import_source",
            table: "planet_wiki_pages",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "import_source",
            table: "planet_wiki_revisions",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "import_source", table: "messages");
        migrationBuilder.DropColumn(name: "import_source", table: "message_reactions");
        migrationBuilder.DropColumn(name: "import_source", table: "planet_threads");
        migrationBuilder.DropColumn(name: "import_source", table: "thread_comments");
        migrationBuilder.DropColumn(name: "import_source", table: "planet_wiki_pages");
        migrationBuilder.DropColumn(name: "import_source", table: "planet_wiki_revisions");
    }
}
