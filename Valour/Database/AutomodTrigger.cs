#nullable enable

using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models.Staff;
using Valour.Shared.Models;

namespace Valour.Database;

public class AutomodTrigger : ISharedAutomodTrigger, ISharedPlanetModel<Guid>
{
    public virtual ICollection<AutomodAction> Actions { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public Guid Id { get; set; }
    public long PlanetId { get; set; }
    public long MemberAddedBy { get; set; }
    public AutomodTriggerType Type { get; set; }
    public string Name { get; set; } = "";
    public string? TriggerWords { get; set; } = "";

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<AutomodTrigger>(e =>
        {
            e.ToTable("automod_triggers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PlanetId).HasColumnName("planet_id");
            e.Property(x => x.MemberAddedBy).HasColumnName("member_added_by");
            e.Property(x => x.Type).HasColumnName("type");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.TriggerWords).HasColumnName("trigger_words");
            e.HasIndex(x => x.PlanetId);

            e.HasMany(x => x.Actions)
                .WithOne(x => x.Trigger)
                .HasForeignKey(x => x.TriggerId);
        });
    }
}
