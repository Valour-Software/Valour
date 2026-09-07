using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

public sealed class VillageTemplate
{
    public int Id { get; set; } = 1;
    public int Revision { get; set; } = 1;
    public int ResetBeforeRevision { get; set; } = 1;
    public long? DraftPlanetId { get; set; }
    public string PublishedJson { get; set; }
    public DateTime? PublishedAt { get; set; }
    public long? PublishedByUserId { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<VillageTemplate>(e =>
        {
            e.ToTable("village_template");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken();
            e.Property(x => x.ResetBeforeRevision).HasColumnName("reset_before_revision");
            e.Property(x => x.DraftPlanetId).HasColumnName("draft_planet_id");
            e.Property(x => x.PublishedJson).HasColumnName("published_json");
            e.Property(x => x.PublishedAt).HasColumnName("published_at");
            e.Property(x => x.PublishedByUserId).HasColumnName("published_by_user_id");
        });
    }
}
