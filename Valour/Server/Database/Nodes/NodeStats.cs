using System.ComponentModel.DataAnnotations;
using Valour.Shared.Nodes;

namespace Valour.Server.Database.Nodes;

[Table("node_stats")]
public class NodeStats : ISharedNodeStats
{
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