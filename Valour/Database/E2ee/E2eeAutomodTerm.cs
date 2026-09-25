using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// One way an automod trigger can match in an encrypted channel: a set of
/// keyed search terms that must all appear in a message. A moderator's client
/// computes these with the channel's index key, so the server can match
/// messages without reading them.
/// </summary>
public class E2eeAutomodTerm
{
    public long Id { get; set; }
    public Guid TriggerId { get; set; }
    public long PlanetId { get; set; }
    public long ChannelId { get; set; }
    public int IndexGeneration { get; set; }
    public int[] Terms { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<E2eeAutomodTerm>(e =>
        {
            e.ToTable("e2ee_automod_terms");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.TriggerId).HasColumnName("trigger_id");
            e.Property(x => x.PlanetId).HasColumnName("planet_id");
            e.Property(x => x.ChannelId).HasColumnName("channel_id");
            e.Property(x => x.IndexGeneration).HasColumnName("index_generation");
            e.Property(x => x.Terms).HasColumnName("terms").IsRequired();
            e.HasIndex(x => new { x.ChannelId, x.IndexGeneration });
            e.HasIndex(x => x.TriggerId);

            // Moving a planet between nodes reads and removes its terms by planet.
            e.HasIndex(x => x.PlanetId);
        });
    }
}
