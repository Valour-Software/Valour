using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("planet_invites")]
public class PlanetInvite : ISharedPlanetInvite
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("PlanetId")]
    public Planet Planet { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    [Key]
    [Column("code")]
    public string Id { get; set; }
    
    [Column("planet_id")]
    public long PlanetId { get; set; }
    
    /// <summary>
    /// The user that created the invite
    /// </summary>
    [Column("issuer_id")]
    public long IssuerId { get; set; }

    /// <summary>
    /// The time the invite was created
    /// </summary>
    [Column("time_created")]
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// When the invite expires
    /// </summary>
    [Column("time_expires")]
    public DateTime? TimeExpires { get; set; }



    public static void SetUpDDModel(ModelBuilder builder)
    {
        builder.Entity<PlanetInvite>(e =>
        {
            // Table
            
            e.ToTable("planet_invites");
            
            // Key

            e.HasKey(x => x.Id);
            
            // Properties

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.IssuerId)
                .HasColumnName("issuer_id");
            
            e.Property(x => x.TimeCreated)
                .HasColumnName("time_created")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc)
                );
            
            e.Property(x => x.TimeExpires)
                .HasColumnName("time_expires")
                .HasConversion(
                    x => x,
                    x => x == null ? null : new DateTime(x.Value.Ticks, DateTimeKind.Utc)
                );
            
            // Relationships

            e.HasOne(x => x.Planet)
                .WithMany(x => x.Invites)
                .HasForeignKey(x => x.PlanetId);
            
            
            // Indices
            
            e.HasIndex(x => new { x.TimeCreated, x.TimeExpires });
            
            e.HasIndex(x => x.PlanetId);

            e.HasIndex(x => x.IssuerId)
                .IsUnique();
            

        });

    }
}
