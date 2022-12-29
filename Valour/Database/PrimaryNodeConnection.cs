using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Valour.Database;

/// <summary>
/// A node connection represents an active primary connection to a node
/// </summary>
[Table("primary_node_connections")]
public class PrimaryNodeConnection
{
    /// <summary>
    /// The SignalR-generated connection id
    /// </summary>
    [Key]
    [Column("connection_id")]
    public string ConnectionId { get; set; }

    /// <summary>
    /// The node the connection is connected to
    /// </summary>
    [Column("node_id")]
    public string NodeId { get; set; }

    /// <summary>
    /// The Id of the user who opened this connection
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }

    /// <summary>
    /// The last time that this connection was opened
    /// or refreshed 
    /// </summary>
    [Column("open_time")]
    public DateTime OpenTime { get; set; }
}
