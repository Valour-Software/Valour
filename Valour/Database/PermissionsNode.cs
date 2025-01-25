using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2024 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

[Table("permissions_nodes")]
public class PermissionsNode : ISharedPermissionsNode
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("PlanetId")]
    public Planet Planet { get; set; }

    [ForeignKey("RoleId")]
    public virtual PlanetRole Role { get; set; }
    
    [ForeignKey("TargetId")]
    public virtual Channel Target { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    [Key]
    [Column("id")]
    public long Id { get; set; }
    
    [Column("planet_id")]
    public long PlanetId { get; set; }

    /// <summary>
    /// The permission code that this node has set
    /// </summary>
    [Column("code")]
    public long Code { get; set; }

    /// <summary>
    /// A mask used to determine if code bits are disabled
    /// </summary>
    [Column("mask")]
    public long Mask { get; set; }

    /// <summary>
    /// The role this permissions node belongs to
    /// </summary>
    [Column("role_id")]
    public long RoleId { get; set; }

    /// <summary>
    /// The id of the object this node applies to
    /// If this is null, it is a base permission
    /// </summary>
    [Column("target_id")]
    public long TargetId { get; set; }

    /// <summary>
    /// The type of object this node applies to
    /// </summary>
    [Column("target_type")]
    public ChannelTypeEnum TargetType { get; set; }


    public static void SetUpDbModel(ModelBuilder builder)
    {
        builder.Entity<PermissionsNode>(e =>
        {
            // ToTable
            e.ToTable("permissions_nodes");
            
            // Key
            e.HasKey(x => x.Id);
            
            // Properties

            e.Property(x => x.Id)
                .HasColumnName("id");
            
            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");
            
            e.Property(x => x.Code)
                .HasColumnName("code");
            
            e.Property(x => x.Mask)
                .HasColumnName("mask");
            
            e.Property(x => x.RoleId)
                .HasColumnName("role_id");
            
            e.Property(x => x.TargetId)
                .HasColumnName("target_id");
            
            e.Property(x => x.TargetType)
                .HasColumnName("target_type");
            
            // Relationships

            e.HasOne(x => x.Planet)
                .WithMany(x => x.PlanetNodes)
                .HasForeignKey(x => x.PlanetId);
            
            
            e.HasOne(x => x.Role)
                .WithMany(x => x.PermissionNodes)
                .HasForeignKey(x => x.RoleId);
            
            e.HasOne(x =>  x.Target)
                .WithMany(x => x.Permissions)
                .HasForeignKey(x => x.TargetId);
        });
    }
}

