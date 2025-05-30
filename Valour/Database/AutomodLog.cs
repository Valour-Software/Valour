using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models.Staff;

namespace Valour.Database;

public class AutomodLog : ISharedAutomodLog
{
    public Guid Id { get; set; }
    public long PlanetId { get; set; }
    public Guid TriggerId { get; set; }
    public long MemberId { get; set; }
    public long? MessageId { get; set; }
    public DateTime TimeTriggered { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<AutomodLog>(e =>
        {
            e.ToTable("automod_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PlanetId).HasColumnName("planet_id");
            e.Property(x => x.TriggerId).HasColumnName("trigger_id");
            e.Property(x => x.MemberId).HasColumnName("member_id");
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.TimeTriggered).HasColumnName("time_triggered");
            e.HasIndex(x => x.PlanetId);
            e.HasIndex(x => x.TriggerId);
            e.HasIndex(x => x.MemberId);
        });
    }
}

