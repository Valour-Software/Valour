using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class MessageMention
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public virtual Message Message { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long Id { get; set; }
    public long MessageId { get; set; }
    public int SortOrder { get; set; }
    public MentionType Type { get; set; }
    public long TargetId { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<MessageMention>(e =>
        {
            e.ToTable("message_mentions");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.MessageId)
                .HasColumnName("message_id")
                .IsRequired();

            e.Property(x => x.SortOrder)
                .HasColumnName("sort_order")
                .IsRequired();

            e.Property(x => x.Type)
                .HasColumnName("type")
                .IsRequired();

            e.Property(x => x.TargetId)
                .HasColumnName("target_id")
                .IsRequired();

            e.HasOne(x => x.Message)
                .WithMany(x => x.Mentions)
                .HasForeignKey(x => x.MessageId);

            e.HasIndex(x => x.MessageId);
            e.HasIndex(x => new { x.Type, x.TargetId });
        });
    }
}
