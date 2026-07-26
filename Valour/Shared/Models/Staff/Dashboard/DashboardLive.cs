namespace Valour.Shared.Models.Staff;

/// <summary>
/// Lightweight live counters pushed to dashboard viewers every few seconds.
/// Everything here must be cheap to compute: indexed counts, Redis scans,
/// and in-process counters only.
/// </summary>
public class DashboardLiveStats
{
    public DateTime TimeUtc { get; set; }

    /// <summary>
    /// Users with activity in the last three minutes (the platform's Online
    /// threshold), excluding bots.
    /// </summary>
    public int OnlineUsers { get; set; }

    /// <summary>
    /// SignalR connections across all cluster nodes.
    /// </summary>
    public int Connections { get; set; }

    /// <summary>
    /// Primary (user-session) connections across all cluster nodes.
    /// </summary>
    public int PrimaryConnections { get; set; }

    /// <summary>
    /// Voice channels with at least one participant, cluster-wide.
    /// </summary>
    public int VoiceChannels { get; set; }

    /// <summary>
    /// Total participants across all active voice channels.
    /// </summary>
    public int VoiceParticipants { get; set; }

    /// <summary>
    /// Planets currently claimed by a cluster node.
    /// </summary>
    public int HostedPlanets { get; set; }

    /// <summary>
    /// Messages recorded in the most recent one-minute stat window.
    /// </summary>
    public int MessagesPerMinute { get; set; }
}

/// <summary>
/// Emitted when a user's first primary connection opens (online) or their
/// last one closes (offline). Deliberately carries no location or device
/// data of any kind.
/// </summary>
public class DashboardPresenceEvent
{
    public long UserId { get; set; }

    /// <summary>
    /// Display name, resolved at broadcast time so viewers don't need a
    /// second lookup.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// True when the user came online, false when they went offline.
    /// </summary>
    public bool Online { get; set; }

    /// <summary>
    /// One of the user's communities, so the dashboard's planet-sphere can
    /// pulse on that community's dot. Null when the user has no planets.
    /// </summary>
    public long? PlanetId { get; set; }

    public DateTime TimeUtc { get; set; }
}
