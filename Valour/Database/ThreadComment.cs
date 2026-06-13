using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models.Threads;

namespace Valour.Database;

public class ThreadComment : ISharedThreadComment
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public PlanetThread Thread { get; set; }
    public ThreadComment ParentComment { get; set; }
    public virtual ICollection<ThreadComment> Replies { get; set; }
    public virtual ICollection<ThreadCommentBoost> Boosts { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long Id { get; set; }
    public long PlanetId { get; set; }
    public long ThreadId { get; set; }
    public long? ParentCommentId { get; set; }
    public int Depth { get; set; }
    public long AuthorUserId { get; set; }
    public long? AuthorMemberId { get; set; }
    public string Content { get; set; }
    public DateTime TimeCreated { get; set; }
    public DateTime? EditedTime { get; set; }
    public int BoostCount { get; set; }
    public int ReplyCount { get; set; }
    public bool IsDeleted { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<ThreadComment>(e =>
        {
            e.ToTable("thread_comments");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.ThreadId)
                .HasColumnName("thread_id");

            e.Property(x => x.ParentCommentId)
                .HasColumnName("parent_comment_id");

            e.Property(x => x.Depth)
                .HasColumnName("depth");

            e.Property(x => x.AuthorUserId)
                .HasColumnName("author_user_id");

            e.Property(x => x.AuthorMemberId)
                .HasColumnName("author_member_id");

            e.Property(x => x.Content)
                .HasColumnName("content")
                .HasMaxLength(ISharedThreadComment.MaxContentLength);

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

            e.Property(x => x.BoostCount)
                .HasColumnName("boost_count");

            e.Property(x => x.ReplyCount)
                .HasColumnName("reply_count");

            e.Property(x => x.IsDeleted)
                .HasColumnName("is_deleted");

            e.HasOne(x => x.Thread)
                .WithMany(x => x.Comments)
                .HasForeignKey(x => x.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.ParentComment)
                .WithMany(x => x.Replies)
                .HasForeignKey(x => x.ParentCommentId);

            e.HasIndex(x => x.ThreadId);
            e.HasIndex(x => new { x.ThreadId, x.ParentCommentId });
            e.HasIndex(x => x.PlanetId);
            e.HasIndex(x => x.AuthorUserId);
        });
    }
}
