using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models.Wiki;

namespace Valour.Database;

/// <summary>
/// Append-only snapshot of a doc page taken on every content-changing save.
/// Revisions die with their page (cascade) — there is no trash in v1.
/// </summary>
public class PlanetWikiRevision
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public PlanetWikiPage Doc { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long Id { get; set; }
    public long PageId { get; set; }
    public long PlanetId { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public long AuthorUserId { get; set; }
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// Server-managed provenance for content imported from another service.
    /// </summary>
    public string ImportSource { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<PlanetWikiRevision>(e =>
        {
            e.ToTable("planet_wiki_revisions");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.PageId)
                .HasColumnName("page_id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.Title)
                .HasColumnName("title")
                .HasMaxLength(ISharedPlanetWikiPage.MaxTitleLength);

            e.Property(x => x.Content)
                .HasColumnName("content")
                .HasMaxLength(ISharedPlanetWikiPage.MaxContentLength);

            e.Property(x => x.AuthorUserId)
                .HasColumnName("author_user_id");

            e.Property(x => x.TimeCreated)
                .HasColumnName("time_created")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc));

            e.Property(x => x.ImportSource)
                .HasColumnName("import_source")
                .HasMaxLength(256);

            e.HasOne(x => x.Doc)
                .WithMany(x => x.Revisions)
                .HasForeignKey(x => x.PageId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.PageId, x.TimeCreated });
            e.HasIndex(x => x.PlanetId);
        });
    }
}
