using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;

namespace Valour.Database;

/// <summary>
/// Represents the state of a channel, used to determine
/// read states and other functions
/// </summary>
[Table("channel_states")]
public class ChannelState : ISharedChannelState
{
    /// <summary>
    /// The id of the channel this state is for
    /// -- This is also the primary key
    /// </summary>
    [Key]
    [Column("channel_id")]
    public long ChannelId { get; set; }
    
    /// <summary>
    /// The id of the planet this state's channel belongs to, if it is in a planet
    /// </summary>
    [Column("planet_id")]
    public long? PlanetId { get; set; }
    
    /// <summary>
    /// The last time at which the channel had a state change which should mark it as
    /// unread to clients
    /// </summary>
    [Column("last_update_time")]
    public DateTime LastUpdateTime { get; set; }
}