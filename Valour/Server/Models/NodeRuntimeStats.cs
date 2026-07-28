namespace Valour.Server.Models;

/// <summary>
/// Runtime stats each node publishes to Redis (`nodestats:{name}`, 120s TTL)
/// every liveness tick. Consumed by the staff dashboard to build cluster-wide
/// counters without asking every node directly.
/// </summary>
public class NodeRuntimeStats
{
    public string Name { get; set; }
    public string Version { get; set; }
    public int Connections { get; set; }
    public int PrimaryConnections { get; set; }
    public int Groups { get; set; }
    public int HostedPlanets { get; set; }

    /// <summary>
    /// Process CPU usage as a percentage of all cores, sampled over the
    /// publish interval. Negative until a second sample exists.
    /// </summary>
    public double CpuPercent { get; set; }

    public double MemoryMb { get; set; }
    public double UptimeSeconds { get; set; }
    public DateTime TimeUtc { get; set; }
}
