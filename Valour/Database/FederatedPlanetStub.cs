using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// A hub-side stub for a planet hosted on a community node. The hub mints the
/// planet's id (keeping the global snowflake id space intact) and stores just
/// enough for discovery, invites, and moderation; the planet's real data lives
/// on the node.
/// </summary>
public class FederatedPlanetStub
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public virtual FederatedNode Node { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    /// <summary>
    /// Hub-minted snowflake — the planet's global id
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The community node hosting this planet
    /// </summary>
    public string NodeDomain { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    /// <summary>
    /// The hub account that owns the planet (accountability)
    /// </summary>
    public long OwnerId { get; set; }

    public int MemberCount { get; set; }

    public bool Nsfw { get; set; }

    /// <summary>
    /// Whether the hosted planet permits self-service joins. This is kept on
    /// the hub so it can never turn a private node planet into a hub-issued
    /// membership merely because the caller knows its snowflake id.
    /// </summary>
    public bool Public { get; set; }

    public bool Discoverable { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Historical compatibility data from older migration flows. Pull-back
    /// authorization is governed by the active node's registered hub owner;
    /// this field is not an identity allow-list.
    /// </summary>
    public string TrustedUserIdsJson { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<FederatedPlanetStub>(e =>
        {
            e.ToTable("federated_planet_stubs");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            e.Property(x => x.NodeDomain)
                .HasColumnName("node_domain")
                .IsRequired();

            e.Property(x => x.Name)
                .HasColumnName("name");

            e.Property(x => x.Description)
                .HasColumnName("description");

            e.Property(x => x.OwnerId)
                .HasColumnName("owner_id")
                .IsRequired();

            e.Property(x => x.MemberCount)
                .HasColumnName("member_count")
                .IsRequired();

            e.Property(x => x.Nsfw)
                .HasColumnName("nsfw")
                .IsRequired();

            e.Property(x => x.Public)
                .HasColumnName("public")
                .HasDefaultValue(false)
                .IsRequired();

            e.Property(x => x.Discoverable)
                .HasColumnName("discoverable")
                .IsRequired();

            e.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            e.Property(x => x.UpdatedAt)
                .HasColumnName("updated_at")
                .IsRequired();

            e.Property(x => x.TrustedUserIdsJson)
                .HasColumnName("trusted_user_ids_json");

            e.HasOne(x => x.Node)
                .WithMany()
                .HasForeignKey(x => x.NodeDomain);

            e.HasIndex(x => x.NodeDomain);
            e.HasIndex(x => x.Discoverable);
        });
    }
}
