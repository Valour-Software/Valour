using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("planets")]
public class Planet : ISharedPlanet
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [InverseProperty("Planet")]
    public virtual ICollection<PlanetRole> Roles { get; set; }

    [InverseProperty("Planet")]
    public virtual ICollection<PlanetMember> Members { get; set; }

    [InverseProperty("Planet")]
    public virtual ICollection<Channel> Channels { get; set; }

    [InverseProperty("Planet")]
    public virtual ICollection<PlanetInvite> Invites { get; set; }

    [InverseProperty("Planet")]
    public virtual ICollection<PlanetEmoji> Emojis { get; set; }

    [InverseProperty("Planet")]
    public virtual ICollection<PlanetRule> Rules { get; set; }

    [InverseProperty("Planet")]
    public virtual ICollection<PlanetReport> Reports { get; set; }

    [InverseProperty("Planet")]
    public virtual ICollection<PlanetThread> Threads { get; set; }
    
    public virtual ICollection<Message> Messages { get; set; }
    
    public virtual ICollection<UserChannelState> UserChannelStates { get; set; }
   
    public virtual ICollection<PlanetTag> Tags { get; set; }
    

    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    [Key]
    [Column("id")]
    public long Id { get; set; }
    
    /// <summary>
    /// The Id of the owner of this planet
    /// </summary>
    [Column("owner_id")]
    public long OwnerId { get; set; }

    /// <summary>
    /// The name of this planet
    /// </summary>
    [Column("name")]
    public string Name { get; set; }

    /// <summary>
    /// True if the planet has a custom icon
    /// </summary>
    [Column("custom_icon")]
    public bool HasCustomIcon { get; set; }
    
    /// <summary>
    /// True if the planet has an animated icon
    /// </summary>
    [Column("animated_icon")]
    public bool HasAnimatedIcon { get; set; }
    
    /// <summary>
    /// The old icon url. Do not use.
    /// </summary>
    [Column("icon_url")]
    public string OldIconUrl { get; set; }

    /// <summary>
    /// The description of the planet
    /// </summary>
    [Column("description")]
    public string Description { get; set; }

    /// <summary>
    /// If the server requires express allowal to join a planet
    /// </summary>
    [Column("public")]
    public bool Public { get; set; }

    /// <summary>
    /// If the server should show up on the discovery tab
    /// </summary>
    [Column("discoverable")]
    public bool Discoverable { get; set; }

    /// <summary>
    /// Soft-delete flag
    /// </summary>
    [Column("is_deleted")]
    public bool IsDeleted { get; set; }
    
    /// <summary>
    /// True if the planet has a custom background
    /// </summary>
    [Column("has_custom_bg")]
    public bool HasCustomBackground { get; set; }
    
    /// <summary>
    /// True if you probably shouldn't be on this server at work owo
    /// </summary>
    [Column("nsfw")]
    public bool Nsfw { get; set; }
    
    [Column("version")]
    public int Version { get; set; }

    /// <summary>
    /// True if the threads feed is enabled for this planet
    /// </summary>
    [Column("enable_threads")]
    public bool EnableThreads { get; set; }

    /// <summary>
    /// True if this planet's threads can be browsed publicly without an account
    /// </summary>
    [Column("public_threads")]
    public bool PublicThreads { get; set; }

    /// <summary>
    /// The id of the single thread pinned to the top of this planet's feed, if any
    /// </summary>
    [Column("pinned_thread_id")]
    public long? PinnedThreadId { get; set; }

    /// <summary>
    /// True when this planet stores media on its own infrastructure
    /// (bring-your-own-storage). Surfaces the "self-hosted media" warning
    /// and icon to users, including pre-join. Mapped fluently in SetupDbModel.
    /// </summary>
    public bool SelfHostedMedia { get; set; }

    /// <summary>
    /// True when this planet runs voice/video calls on its own LiveKit SFU
    /// (bring-your-own-voice). Surfaces the "community-hosted voice" warning
    /// to users before they join a call. Mapped fluently in SetupDbModel.
    /// </summary>
    public bool SelfHostedVoice { get; set; }

    /// <summary>
    /// True while a migration is in progress — the planet is read-only during
    /// the snapshot→handoff window so no writes are lost. Cleared on completion
    /// (the planet is deleted) or abort. Server-internal (not on the wire).
    /// </summary>
    public bool LockedForMigration { get; set; }

    // Only to fulfill contract
    [NotMapped]
    public new string NodeName { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<Planet>(e =>
        {
            e.Property(x => x.SelfHostedMedia)
                .HasColumnName("self_hosted_media")
                .HasDefaultValue(false)
                .IsRequired();

            e.Property(x => x.SelfHostedVoice)
                .HasColumnName("self_hosted_voice")
                .HasDefaultValue(false)
                .IsRequired();

            e.Property(x => x.LockedForMigration)
                .HasColumnName("locked_for_migration")
                .HasDefaultValue(false)
                .IsRequired();
        });
    }
}
