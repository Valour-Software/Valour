using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Nodes;

namespace Valour.Database;

public class NodeStats : ISharedNodeStats
{
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    public string Name { get; set; }
    public int ConnectionCount { get; set; }
    public int ConnectionGroupCount { get; set; }
    public int PlanetCount { get; set; }
    public int ActiveMemberCount { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
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
        });
    }
}