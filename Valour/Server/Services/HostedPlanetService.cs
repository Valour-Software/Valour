using System.Collections.Concurrent;

namespace Valour.Server.Services;

public class HostedPlanetService
{
    /// <summary>
    /// A cache that holds planets hosted by this node. Nodes keep their hosted
    /// planets in-memory to reduce database load.
    /// </summary>
    private readonly ConcurrentDictionary<long, HostedPlanet> _hostedPlanetCache = new();
} 