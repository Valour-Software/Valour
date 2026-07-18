using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using Valour.Shared.Models.Wiki;

namespace Valour.Database;

public class PlanetWikiPage : ISharedPlanetWikiPage
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public Planet Planet { get; set; }
    public PlanetWikiPage Parent { get; set; }
    public virtual ICollection<PlanetWikiPage> Children { get; set; }
    public virtual ICollection<PlanetWikiRevision> Revisions { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long Id { get; set; }
    public long PlanetId { get; set; }
    public long? ParentId { get; set; }
    public bool IsFolder { get; set; }
    public string Slug { get; set; }
    public string PreviousSlug { get; set; }
    public string Title { get; set; }

    /// <summary>
    /// Markdown content. Null for folders. Not part of the wire metadata
    /// model — served through dedicated content endpoints.
    /// </summary>
    public string Content { get; set; }

    public uint Position { get; set; }
    public bool IsPublished { get; set; }
    public long Version { get; set; }
    public DateTime TimeCreated { get; set; }
    public DateTime? LastEdited { get; set; }
    public long CreatedByUserId { get; set; }
    public long? LastEditedByUserId { get; set; }

    /// <summary>
    /// Generated tsvector over title + content for full-text search
    /// </summary>
    public NpgsqlTsVector SearchVector { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<PlanetWikiPage>(e =>
        {
            e.ToTable("planet_wiki_pages");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.ParentId)
                .HasColumnName("parent_id");

            e.Property(x => x.IsFolder)
                .HasColumnName("is_folder");

            e.Property(x => x.Slug)
                .HasColumnName("slug")
                .HasMaxLength(ISharedPlanetWikiPage.MaxSlugLength);

            e.Property(x => x.PreviousSlug)
                .HasColumnName("previous_slug")
                .HasMaxLength(ISharedPlanetWikiPage.MaxSlugLength);

            e.Property(x => x.Title)
                .HasColumnName("title")
                .HasMaxLength(ISharedPlanetWikiPage.MaxTitleLength);

            e.Property(x => x.Content)
                .HasColumnName("content")
                .HasMaxLength(ISharedPlanetWikiPage.MaxContentLength);

            e.Property(x => x.Position)
                .HasColumnName("position");

            e.Property(x => x.IsPublished)
                .HasColumnName("is_published")
                .HasDefaultValue(true);

            e.Property(x => x.Version)
                .HasColumnName("version");

            e.Property(x => x.TimeCreated)
                .HasColumnName("time_created")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc));

            e.Property(x => x.LastEdited)
                .HasColumnName("last_edited")
                .HasConversion(
                    x => x,
                    x => x.HasValue ? new DateTime(x.Value.Ticks, DateTimeKind.Utc) : null);

            e.Property(x => x.CreatedByUserId)
                .HasColumnName("created_by_user_id");

            e.Property(x => x.LastEditedByUserId)
                .HasColumnName("last_edited_by_user_id");

            e.Property(x => x.SearchVector)
                .HasColumnName("search_vector");

            // 'simple' config: planets are multilingual, so predictable
            // non-stemmed matching beats english-only stemming
            e.HasGeneratedTsVectorColumn(
                    x => x.SearchVector,
                    "simple",
                    x => new { x.Title, x.Content });

            e.HasOne(x => x.Planet)
                .WithMany(x => x.WikiPages)
                .HasForeignKey(x => x.PlanetId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.PlanetId);

            e.HasIndex(x => new { x.PlanetId, x.Slug })
                .IsUnique()
                .HasFilter("slug IS NOT NULL");

            e.HasIndex(x => new { x.PlanetId, x.PreviousSlug })
                .HasFilter("previous_slug IS NOT NULL");

            e.HasIndex(x => new { x.PlanetId, x.ParentId, x.Position });

            e.HasIndex(x => x.SearchVector)
                .HasMethod("GIN");
        });
    }
}
