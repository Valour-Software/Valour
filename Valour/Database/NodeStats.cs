using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Nodes;

namespace Valour.Database;

[Table("node_stats")]
public class NodeStats : ISharedNodeStats
{
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    [Key]
    [Column("name")]
    public string Name { get; set; }

    [Column("connection_count")]
    public int ConnectionCount { get; set; }

    [Column("connection_group_count")]
    public int ConnectionGroupCount { get; set; }

    [Column("planet_count")]
    public int PlanetCount { get; set; }

    [Column("active_member_count")]
    public int ActiveMemberCount { get; set; }

    public static void SetUpDDModel(ModelBuilder builder)
    {
        builder.Entity<NodeStats>(e =>
        {
            // ToTable
            e.ToTable("node_stats");
            
            // Key
            e.HasKey(x => x.Name);
            
            // Properties   
            e.Property(x => x.Name)
                .HasColumnName("name");
            
            e.Property(x => x.ConnectionCount)
                .HasColumnName("connection_count");
            
            e.Property(x => x.ConnectionGroupCount)
                .HasColumnName("connection_group_count");
            
            e.Property(x => x.PlanetCount)
                .HasColumnName("planet_count");
            
            e.Property(x => x.ActiveMemberCount)
                .HasColumnName("active_member_count");
            
            // Relationships
            
            // Indices
            
            e.HasIndex(x => x.Name);
            
        });
    }
}