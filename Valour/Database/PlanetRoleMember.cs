using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("planet_role_members")]
public class PlanetRoleMember : ISharedPlanetRoleMember
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("PlanetId")]
    public virtual Planet Planet { get; set; }
    
    [ForeignKey("MemberId")]
    public virtual PlanetMember Member { get; set; }
    
    [ForeignKey("RoleId")]
    public virtual PlanetRole Role { get; set; }

    [ForeignKey("UserId")]
    public virtual User User { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    [Key]
    [Column("id")]
    public long Id { get; set; }
    
    [Column("planet_id")]
    public long PlanetId { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("role_id")]
    public long RoleId { get; set; }

    [Column("member_id")]
    public long MemberId { get; set; }

    public static void SetUpDDModel(ModelBuilder builder)
    {
        builder.Entity<PlanetRoleMember>(e =>
        {
            // ToTable
            e.ToTable("planet_role_members");

            // key
            e.HasKey(x => x.Id);

            // Properties

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.UserId)
                .HasColumnName("user_id");

            e.Property(x => x.RoleId)
                .HasColumnName("role_id");

            e.Property(x => x.MemberId)
                .HasColumnName("member_id");

            // Relationships

            e.HasOne(x => x.Planet)
                .WithMany(x => x.RoleMembers)
                .HasForeignKey(x => x.PlanetId);
            
            e.HasOne(x => x.Member)
                .WithMany(x => x.RoleMembership)
                .HasForeignKey(x => x.MemberId);
            
            e.HasOne(x => x.Role)
                .WithMany(x => x.Roles)
                .HasForeignKey(x => x.RoleId);
            
            e.HasOne(x => x.User)
                .WithMany(x => x.Memberships)
                .HasForeignKey(x => x.MemberId);
            
            // Indices

            e.HasIndex(x => x.Id);

            e.HasIndex(x => x.PlanetId);

            e.HasIndex(x => x.MemberId);
            
            e.HasIndex(x => x.RoleId);

            e.HasIndex(x => x.UserId);
        });
    }
}

