using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models.Threads;

namespace Valour.Database;

public class ThreadBoost : ISharedThreadBoost
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public PlanetThread Thread { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long Id { get; set; }
    public long ThreadId { get; set; }
    public long PlanetId { get; set; }
    public long UserId { get; set; }
    public DateTime CreatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<ThreadBoost>(e =>
        {
            e.ToTable("thread_boosts");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.ThreadId)
                .HasColumnName("thread_id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.UserId)
                .HasColumnName("user_id");

            e.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc));

            e.HasOne(x => x.Thread)
                .WithMany(x => x.Boosts)
                .HasForeignKey(x => x.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.ThreadId, x.UserId })
                .IsUnique();
            e.HasIndex(x => x.UserId);
        });
    }
}

public class ThreadCommentBoost : ISharedThreadCommentBoost
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public ThreadComment Comment { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long Id { get; set; }
    public long CommentId { get; set; }
    public long ThreadId { get; set; }
    public long PlanetId { get; set; }
    public long UserId { get; set; }
    public DateTime CreatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<ThreadCommentBoost>(e =>
        {
            e.ToTable("thread_comment_boosts");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.CommentId)
                .HasColumnName("comment_id");

            e.Property(x => x.ThreadId)
                .HasColumnName("thread_id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.UserId)
                .HasColumnName("user_id");

            e.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc));

            e.HasOne(x => x.Comment)
                .WithMany(x => x.Boosts)
                .HasForeignKey(x => x.CommentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.CommentId, x.UserId })
                .IsUnique();
            e.HasIndex(x => x.ThreadId);
            e.HasIndex(x => x.UserId);
        });
    }
}
