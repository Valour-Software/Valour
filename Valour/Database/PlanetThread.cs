using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models.Threads;

namespace Valour.Database;

public class PlanetThread : ISharedPlanetThread
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public Planet Planet { get; set; }
    public virtual ICollection<ThreadComment> Comments { get; set; }
    public virtual ICollection<ThreadAttachment> Attachments { get; set; }
    public virtual ICollection<ThreadBoost> Boosts { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long Id { get; set; }
    public long PlanetId { get; set; }
    public long AuthorUserId { get; set; }
    public long? AuthorMemberId { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public DateTime TimeCreated { get; set; }
    public DateTime? EditedTime { get; set; }
    public bool IsLocked { get; set; }
    public bool IsPinned { get; set; }
    public bool Nsfw { get; set; }
    public int BoostCount { get; set; }
    public int CommentCount { get; set; }
    public bool IsDeleted { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<PlanetThread>(e =>
        {
            e.ToTable("planet_threads");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.AuthorUserId)
                .HasColumnName("author_user_id");

            e.Property(x => x.AuthorMemberId)
                .HasColumnName("author_member_id");

            e.Property(x => x.Title)
                .HasColumnName("title")
                .HasMaxLength(ISharedPlanetThread.MaxTitleLength);

            e.Property(x => x.Content)
                .HasColumnName("content")
                .HasMaxLength(ISharedPlanetThread.MaxContentLength);

            e.Property(x => x.TimeCreated)
                .HasColumnName("time_created")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc));

            e.Property(x => x.EditedTime)
                .HasColumnName("edited_time")
                .HasConversion(
                    x => x,
                    x => x.HasValue ? new DateTime(x.Value.Ticks, DateTimeKind.Utc) : null);

            e.Property(x => x.IsLocked)
                .HasColumnName("is_locked");

            e.Property(x => x.IsPinned)
                .HasColumnName("is_pinned");

            e.Property(x => x.Nsfw)
                .HasColumnName("nsfw");

            e.Property(x => x.BoostCount)
                .HasColumnName("boost_count");

            e.Property(x => x.CommentCount)
                .HasColumnName("comment_count");

            e.Property(x => x.IsDeleted)
                .HasColumnName("is_deleted");

            e.HasOne(x => x.Planet)
                .WithMany(x => x.Threads)
                .HasForeignKey(x => x.PlanetId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.PlanetId);
            e.HasIndex(x => new { x.PlanetId, x.TimeCreated });
            e.HasIndex(x => new { x.PlanetId, x.IsPinned });
            e.HasIndex(x => x.AuthorUserId);
        });
    }
}
