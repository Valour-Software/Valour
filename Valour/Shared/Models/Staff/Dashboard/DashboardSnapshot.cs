namespace Valour.Shared.Models.Staff;

/// <summary>
/// Full point-in-time platform snapshot for the staff dashboard, fetched once
/// on load and refreshed occasionally. Fast-moving counters ride on
/// <see cref="DashboardLiveStats"/> instead.
/// </summary>
public class DashboardSnapshot
{
    public DateTime TimeUtc { get; set; }

    public DashboardLiveStats Live { get; set; }

    /// <summary>
    /// All registered (non-bot) accounts.
    /// </summary>
    public long TotalUsers { get; set; }

    public long TotalPlanets { get; set; }

    public long TotalMessages { get; set; }

    /// <summary>
    /// Distinct users active today (UTC).
    /// </summary>
    public int DailyActiveUsers { get; set; }

    /// <summary>
    /// Distinct users active in the last 30 days.
    /// </summary>
    public int MonthlyActiveUsers { get; set; }

    /// <summary>
    /// New accounts created today (UTC).
    /// </summary>
    public int SignupsToday { get; set; }

    /// <summary>
    /// Planets with at least one member connected in the last 15 minutes.
    /// </summary>
    public int ActivePlanets { get; set; }

    public List<DashboardVoiceChannel> ActiveVoiceChannels { get; set; }

    public List<DashboardTopPlanet> TopPlanets { get; set; }

    /// <summary>
    /// Communities rendered as dots on the dashboard's abstract planet-sphere,
    /// largest first. Capped server-side (~300) to keep the payload small.
    /// </summary>
    public List<DashboardGlobePlanet> GlobePlanets { get; set; }

    public List<DashboardNodeInfo> ClusterNodes { get; set; }

    public DashboardFederationInfo Federation { get; set; }

    public DashboardRevenue Revenue { get; set; }
}

public class DashboardVoiceChannel
{
    public long ChannelId { get; set; }
    public string ChannelName { get; set; }
    public long? PlanetId { get; set; }
    public string PlanetName { get; set; }
    public int ParticipantCount { get; set; }
}

/// <summary>
/// A community dot on the abstract planet-sphere. Position is derived
/// client-side from the planet id, so the payload carries only identity
/// and size.
/// </summary>
public class DashboardGlobePlanet
{
    public long PlanetId { get; set; }
    public string Name { get; set; }
    public int MemberCount { get; set; }

    /// <summary>
    /// Members connected within the last 15 minutes.
    /// </summary>
    public int OnlineCount { get; set; }
}

public class DashboardTopPlanet
{
    public long PlanetId { get; set; }
    public string Name { get; set; }

    /// <summary>
    /// Fully-resolved icon URL, or null when the planet uses the default icon.
    /// </summary>
    public string IconUrl { get; set; }

    public int MemberCount { get; set; }

    /// <summary>
    /// Members connected within the last 15 minutes.
    /// </summary>
    public int ActiveNow { get; set; }

    /// <summary>
    /// Members connected within the last 24 hours.
    /// </summary>
    public int ActiveToday { get; set; }

    /// <summary>
    /// Messages sent in the last 24 hours.
    /// </summary>
    public int MessagesToday { get; set; }
}

/// <summary>
/// One official cluster node, assembled from the Redis liveness keys plus the
/// per-node runtime stats each node publishes.
/// </summary>
public class DashboardNodeInfo
{
    public string Name { get; set; }

    /// <summary>
    /// True for the node that served this snapshot.
    /// </summary>
    public bool IsSelf { get; set; }

    public bool Alive { get; set; }

    public DateTime LastSeenUtc { get; set; }

    public int Connections { get; set; }
    public int PrimaryConnections { get; set; }
    public int Groups { get; set; }
    public int HostedPlanets { get; set; }

    /// <summary>
    /// Process CPU usage as a percentage of all cores, sampled over the node's
    /// stats-publish interval. Negative when unknown.
    /// </summary>
    public double CpuPercent { get; set; }

    /// <summary>
    /// Process working set in megabytes. Negative when unknown.
    /// </summary>
    public double MemoryMb { get; set; }

    public double UptimeSeconds { get; set; }

    public string Version { get; set; }
}

/// <summary>
/// Community / self-hosted federated node registry summary. Counts only —
/// the full registry has its own staff surface.
/// </summary>
public class DashboardFederationInfo
{
    public int TotalNodes { get; set; }
    public int VerifiedNodes { get; set; }
    public int PendingNodes { get; set; }
    public int SuspendedNodes { get; set; }

    /// <summary>
    /// Verified nodes seen by the hub within the last 7 days.
    /// </summary>
    public int ActiveLast7Days { get; set; }
}

/// <summary>
/// Real-money revenue summary. USD amounts are derived: Stripe never has its
/// amounts persisted locally, so cents are mapped from subscription tier
/// constants and credit-pack product definitions.
/// </summary>
public class DashboardRevenue
{
    /// <summary>
    /// Monthly recurring revenue in USD cents from active Stripe subscriptions.
    /// </summary>
    public long MrrCents { get; set; }

    public int ActiveStripeSubscriptions { get; set; }

    /// <summary>
    /// Subscriptions paid with in-app Valour Credits rather than real money.
    /// </summary>
    public int ActiveVcSubscriptions { get; set; }

    /// <summary>
    /// Real-money revenue recorded today (UTC): credit purchases plus
    /// subscription charges.
    /// </summary>
    public long TodayCents { get; set; }

    /// <summary>
    /// Real-money revenue recorded in the last 30 days.
    /// </summary>
    public long Last30DaysCents { get; set; }

    public List<DashboardRevenueTier> Tiers { get; set; }
}

public class DashboardRevenueTier
{
    public string Type { get; set; }
    public int StripeCount { get; set; }
    public int VcCount { get; set; }

    /// <summary>
    /// USD cents per month contributed by this tier's Stripe subscriptions.
    /// </summary>
    public long MrrCents { get; set; }
}
