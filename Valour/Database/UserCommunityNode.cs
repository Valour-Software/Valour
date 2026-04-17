using Microsoft.EntityFrameworkCore;
using Valour.Shared.Nodes;

namespace Valour.Database;

public class UserCommunityNode
{
    public virtual User User { get; set; }

    public long Id { get; set; }
    public long UserId { get; set; }
    public string NodeId { get; set; }
    public string Name { get; set; }
    public string CanonicalOrigin { get; set; }
    public string AuthorityOrigin { get; set; }
    public NodeMode Mode { get; set; } = NodeMode.Community;
    public DateTime TimeAdded { get; set; } = DateTime.UtcNow;

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<UserCommunityNode>(e =>
        {
            e.ToTable("user_community_nodes");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.UserId)
                .HasColumnName("user_id");

            e.Property(x => x.NodeId)
                .HasColumnName("node_id");

            e.Property(x => x.Name)
                .HasColumnName("name");

            e.Property(x => x.CanonicalOrigin)
                .HasColumnName("canonical_origin");

            e.Property(x => x.AuthorityOrigin)
                .HasColumnName("authority_origin");

            e.Property(x => x.Mode)
                .HasColumnName("mode");

            e.Property(x => x.TimeAdded)
                .HasColumnName("time_added")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc)
                );

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId);

            e.HasIndex(x => x.UserId);
            e.HasIndex(x => new { x.UserId, x.CanonicalOrigin })
                .IsUnique();
        });
    }
}
