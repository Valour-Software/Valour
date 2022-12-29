using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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
}