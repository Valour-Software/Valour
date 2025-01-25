using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// A node connection represents an active primary connection to a node
/// </summary>
[Table("primary_node_connections")]
public class PrimaryNodeConnection
{
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
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


    public static void SetUpDbModel(ModelBuilder builder)
    {
        builder.Entity<PrimaryNodeConnection>(e =>
        {
            // ToTable
            e.ToTable("primary_node_connections");

            // Key
            e.HasKey(x => x.ConnectionId);

            // Properties

            e.Property(x => x.ConnectionId)
                .HasColumnName("connection_id");
            
            e.Property(x => x.NodeId)
                .HasColumnName("node_id");

            e.Property(x => x.UserId)
                .HasColumnName("user_id");

            e.Property(x => x.OpenTime)
                .HasColumnName("open_time")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc)
                    );
            
            // Relationships
            
            // Indices
            
            e.HasIndex(x => x.UserId);
        });
    }
}
