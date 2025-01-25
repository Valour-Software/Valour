using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("planet_bans")]
public class PlanetBan : ISharedPlanetBan
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
    [Column("id")]
    public long Id { get; set; }
    
    [Column("planet_id")]
    public long PlanetId { get; set; }

    /// <summary>
    /// The member that banned the user
    /// </summary>
    [Column("issuer_id")]
    public long IssuerId { get; set; }

    /// <summary>
    /// The userId of the target that was banned
    /// </summary>
    [Column("target_id")]
    public long TargetId { get; set; }

    /// <summary>
    /// The reason for the ban
    /// </summary>
    [Column("reason")]
    public string Reason { get; set; }

    /// <summary>
    /// The time the ban was placed
    /// </summary>
    [Column("time_created")]
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// The time the ban expires. Null for permanent.
    /// </summary>
    [Column("time_expires")]
    public DateTime? TimeExpires { get; set; }

    public static void setUpDbModel(ModelBuilder builder)
    {
        builder.Entity<PlanetBan>(e =>
        {
            // ToTable
            e.ToTable("planet_bans");
            
            // Key
            e.HasKey(x => x.Id);
            
            // Properties
            e.Property(x => x.Id)
                .HasColumnName("id");
            
            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");
            
            e.Property(x => x.IssuerId)
                .HasColumnName("issuer_id");
            
            e.Property(x => x.TargetId)
                .HasColumnName("target_id");
            
            e.Property(x => x.Reason)
                .HasColumnName("reason");

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
                .WithMany(x => x.BanRole)
                .HasForeignKey(x => x.PlanetId);
            
            // Indices
            
            e.HasIndex(x => x.Id)
                .IsUnique();

            e.HasIndex(x => x.IssuerId);
            
            e.HasIndex(x => x.TargetId);
            
            e.HasIndex(x => x.PlanetId);

        });
    }

}
