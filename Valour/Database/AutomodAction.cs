#nullable enable

using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models.Staff;
using Valour.Shared.Models;

namespace Valour.Database;

public class AutomodAction : ISharedAutomodAction
{
    public virtual AutomodTrigger Trigger { get; set; }
    
    public Guid Id { get; set; }
    public Guid? PriorAction { get; set; }
    public Guid TriggerId { get; set; }
    public long MemberAddedBy { get; set; }
    public AutomodActionType ActionType { get; set; }
    public long PlanetId { get; set; }
    public long TargetMemberId { get; set; }
    public long? MessageId { get; set; }
    public long? RoleId { get; set; }
    public DateTime? Expires { get; set; }
    public string Reason { get; set; } = "";
    public string Message { get; set; } = "";

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<AutomodAction>(e =>
        {
            e.ToTable("automod_actions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PriorAction).HasColumnName("prior_action");
            e.Property(x => x.TriggerId).HasColumnName("trigger_id");
            e.Property(x => x.MemberAddedBy).HasColumnName("member_added_by");
            e.Property(x => x.ActionType).HasColumnName("action_type");
            e.Property(x => x.PlanetId).HasColumnName("planet_id");
            e.Property(x => x.TargetMemberId).HasColumnName("target_member_id");
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.RoleId).HasColumnName("role_id");
            e.Property(x => x.Expires).HasColumnName("expires");
            e.Property(x => x.Reason).HasColumnName("reason");
            e.Property(x => x.Message).HasColumnName("message");
            e.HasIndex(x => x.TriggerId);
            e.HasIndex(x => x.PlanetId);

            e.HasOne(x => x.Trigger)
                .WithMany(x => x.Actions)
                .HasForeignKey(x => x.TriggerId);
        });
    }
}
