namespace Valour.Shared.Utilities;

public enum ValourRouteType
{
    Unknown,
    PlanetChannel,
    DirectChannel,
    PlanetThread,
    PlanetThreadFeed,
    Friends,
}

/// <summary>
/// A parsed Valour deep link. Describes which in-app destination a Valour URL
/// (or relative route) points to.
/// </summary>
public readonly struct ValourRoute
{
    public ValourRouteType Type { get; init; }
    public long? PlanetId { get; init; }
    public long? ChannelId { get; init; }
    public long? ThreadId { get; init; }
    public long? MessageId { get; init; }
}

/// <summary>
/// Parses Valour links and relative routes into a structured <see cref="ValourRoute"/>.
/// Shared between the client (deep-link routing, in-app link rendering) and the
/// server (thread link previews) so the route formats stay in lockstep.
/// </summary>
public static class ValourRouteParser
{
    private const string ThreadsHost = "threads.valour.gg";

    private static readonly HashSet<string> ValourHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "valour.gg",
        "www.valour.gg",
        "app.valour.gg",
        ThreadsHost,
    };

    /// <summary>
    /// True if the host belongs to Valour's web app.
    /// </summary>
    public static bool IsValourHost(string? host) =>
        !string.IsNullOrWhiteSpace(host) && ValourHosts.Contains(host);

    /// <summary>
    /// True if the given value is a Valour app link (an absolute Valour URL or a
    /// relative route that maps to a known in-app destination).
    /// </summary>
    public static bool IsValourAppLink(string? urlOrPath) =>
        TryParse(urlOrPath, out var route) && route.Type != ValourRouteType.Unknown;

    /// <summary>
    /// Attempts to parse a Valour URL or relative route into a destination.
    /// Absolute URLs must point at a Valour host. Relative routes are always considered.
    /// </summary>
    public static bool TryParse(string? urlOrPath, out ValourRoute route)
    {
        route = default;

        if (string.IsNullOrWhiteSpace(urlOrPath))
            return false;

        string path;
        var isThreadsHost = false;

        if (Uri.TryCreate(urlOrPath, UriKind.Absolute, out var absolute))
        {
            if (!IsValourHost(absolute.Host))
                return false;

            isThreadsHost = absolute.Host.Equals(ThreadsHost, StringComparison.OrdinalIgnoreCase);
            path = absolute.AbsolutePath;
        }
        else
        {
            // Relative route - strip any query/fragment
            path = urlOrPath;
            var queryIndex = path.IndexOfAny(new[] { '?', '#' });
            if (queryIndex >= 0)
                path = path[..queryIndex];
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        // The threads subdomain uses clean URLs without a route prefix:
        // threads.valour.gg/{planetId}/{threadId} and threads.valour.gg/{planetId}
        if (isThreadsHost && long.TryParse(parts[0], out var threadsHostPlanet))
        {
            if (parts.Length >= 2 && long.TryParse(parts[1], out var threadsHostThread))
            {
                route = new ValourRoute
                {
                    Type = ValourRouteType.PlanetThread,
                    PlanetId = threadsHostPlanet,
                    ThreadId = threadsHostThread,
                };
                return true;
            }

            route = new ValourRoute
            {
                Type = ValourRouteType.PlanetThreadFeed,
                PlanetId = threadsHostPlanet,
            };
            return true;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "planetchannels":
                // /planetchannels/{planetId}/{channelId}/{messageId?}
                if (parts.Length >= 3 &&
                    long.TryParse(parts[1], out var pcPlanet) &&
                    long.TryParse(parts[2], out var pcChannel))
                {
                    long? messageId = null;
                    if (parts.Length >= 4 && long.TryParse(parts[3], out var pcMessage))
                        messageId = pcMessage;

                    route = new ValourRoute
                    {
                        Type = ValourRouteType.PlanetChannel,
                        PlanetId = pcPlanet,
                        ChannelId = pcChannel,
                        MessageId = messageId,
                    };
                    return true;
                }
                break;

            case "directchannels":
                // /directchannels/{channelId}/{messageId?}
                if (parts.Length >= 2 && long.TryParse(parts[1], out var dcChannel))
                {
                    long? messageId = null;
                    if (parts.Length >= 3 && long.TryParse(parts[2], out var dcMessage))
                        messageId = dcMessage;

                    route = new ValourRoute
                    {
                        Type = ValourRouteType.DirectChannel,
                        ChannelId = dcChannel,
                        MessageId = messageId,
                    };
                    return true;
                }
                break;

            // /planetthreads/{planetId}/{threadId?} (in-app) and
            // /threads/{planetId}/{threadId} (public server-rendered page)
            case "planetthreads":
            case "threads":
                if (parts.Length >= 2 && long.TryParse(parts[1], out var threadPlanet))
                {
                    if (parts.Length >= 3 && long.TryParse(parts[2], out var threadId))
                    {
                        route = new ValourRoute
                        {
                            Type = ValourRouteType.PlanetThread,
                            PlanetId = threadPlanet,
                            ThreadId = threadId,
                        };
                        return true;
                    }

                    // Thread feed for a planet only makes sense for the in-app route
                    if (parts[0].Equals("planetthreads", StringComparison.OrdinalIgnoreCase))
                    {
                        route = new ValourRoute
                        {
                            Type = ValourRouteType.PlanetThreadFeed,
                            PlanetId = threadPlanet,
                        };
                        return true;
                    }
                }
                break;

            case "friends":
                route = new ValourRoute { Type = ValourRouteType.Friends };
                return true;
        }

        return false;
    }
}
