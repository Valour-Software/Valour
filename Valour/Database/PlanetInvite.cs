using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class PlanetInvite : ISharedPlanetInvite
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    public Planet Planet { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    public string Id { get; set; }
    
    public long PlanetId { get; set; }
    
    /// <summary>
    /// The user that created the invite
    /// </summary>
    public long IssuerId { get; set; }

    /// <summary>
    /// The time the invite was created
    /// </summary>
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// When the invite expires
    /// </summary>
    public DateTime? TimeExpires { get; set; }
    
    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<PlanetInvite>(e =>
        {
            // Table
            
            e.ToTable("planet_invites");
            
            // Key

            e.HasKey(x => x.Id);
            
            // Properties

            e.Property(x => x.Id)
                .HasColumnName("code");

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

            e.HasIndex(x => x.IssuerId);
        });

    }
}
