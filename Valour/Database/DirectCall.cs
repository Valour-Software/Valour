using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class DirectCall
{
    public long Id { get; set; }
    public long ChannelId { get; set; }
    public long CallerUserId { get; set; }
    public DirectCallKind Kind { get; set; }
    public DirectCallState State { get; set; }
    public DirectCallEndReason EndReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public virtual Channel Channel { get; set; }
    public virtual User CallerUser { get; set; }
    public virtual List<DirectCallMember> Members { get; set; } = [];

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<DirectCall>(e =>
        {
            e.ToTable("direct_calls");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ChannelId).HasColumnName("channel_id");
            e.Property(x => x.CallerUserId).HasColumnName("caller_user_id");
            e.Property(x => x.Kind).HasColumnName("kind");
            e.Property(x => x.State).HasColumnName("state");
            e.Property(x => x.EndReason).HasColumnName("end_reason");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
            e.Property(x => x.EndedAt).HasColumnName("ended_at");
            e.HasOne(x => x.Channel).WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CallerUser).WithMany().HasForeignKey(x => x.CallerUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.State, x.ExpiresAt });
            e.HasIndex(x => x.ChannelId);
        });
    }
}

public class DirectCallMember
{
    public long Id { get; set; }
    public long CallId { get; set; }
    public long UserId { get; set; }
    public bool IsCaller { get; set; }
    public DirectCallMemberState State { get; set; }
    public DateTime? RespondedAt { get; set; }
    public virtual DirectCall Call { get; set; }
    public virtual User User { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<DirectCallMember>(e =>
        {
            e.ToTable("direct_call_members");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CallId).HasColumnName("call_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.IsCaller).HasColumnName("is_caller");
            e.Property(x => x.State).HasColumnName("state");
            e.Property(x => x.RespondedAt).HasColumnName("responded_at");
            e.HasOne(x => x.Call).WithMany(x => x.Members).HasForeignKey(x => x.CallId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CallId, x.UserId }).IsUnique();
            // Database-level busy guard; prevents simultaneous call starts on
            // different application nodes from inviting/joining the same user.
            e.HasIndex(x => x.UserId)
                .IsUnique()
                .HasFilter("state IN (0, 1)");
        });
    }
}
