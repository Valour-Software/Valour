namespace Valour.Shared.Utilities;

public enum ValourRouteType
{
    Unknown,
    PlanetChannel,
    DirectChannel,
    PlanetThread,
    PlanetThreadFeed,
    Friends,
    PlanetWiki,
    PlanetWikiPage,
    Invite,
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
    public long? PageId { get; init; }

    /// <summary>
    /// Page slug for public docs URLs (the parser cannot resolve slugs to ids)
    /// </summary>
    public string? PageSlug { get; init; }

    /// <summary>
    /// Docs vanity name when the URL identified the planet by vanity rather
    /// than id. Vanities are never all-digits, so the two never collide.
    /// </summary>
    public string? Vanity { get; init; }

    /// <summary>
    /// Invite code for invite links (/i/{code}).
    /// </summary>
    public string? InviteCode { get; init; }
}

/// <summary>
/// Parses Valour links and relative routes into a structured <see cref="ValourRoute"/>.
/// Shared between the client (deep-link routing, in-app link rendering) and the
/// server (thread link previews) so the route formats stay in lockstep.
/// </summary>
public static class ValourRouteParser
{
    /// <summary>
    /// True if the host belongs to this deployment's web app
    /// (see Hosting.ValourHosts for the configured hosts).
    /// </summary>
    public static bool IsValourHost(string? host) =>
        !string.IsNullOrWhiteSpace(host) && Hosting.ValourHosts.IsSelfHost(host);

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
        var isWikiHost = false;

        if (Uri.TryCreate(urlOrPath, UriKind.Absolute, out var absolute))
        {
            if (!IsValourHost(absolute.Host))
                return false;

            isThreadsHost = absolute.Host.Equals(Hosting.ValourHosts.ThreadsHost, StringComparison.OrdinalIgnoreCase);
            isWikiHost = absolute.Host.Equals(Hosting.ValourHosts.WikiHost, StringComparison.OrdinalIgnoreCase);
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

        // The docs subdomain uses clean URLs without a route prefix:
        // wiki.valour.gg/{planetIdOrVanity} and wiki.valour.gg/{planetIdOrVanity}/{pageSlug}
        if (isWikiHost && TryParseWikiSegments(parts, 0, out route))
            return true;

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

            // /docs/{planetIdOrVanity}/{pageSlug?} (public server-rendered page)
            case "docs":
                if (TryParseWikiSegments(parts, 1, out route))
                    return true;
                break;

            // /planetwiki/{planetId}/{pageId?} (in-app)
            case "planetwiki":
                if (parts.Length >= 2 && long.TryParse(parts[1], out var docsPlanet))
                {
                    if (parts.Length >= 3 && long.TryParse(parts[2], out var pageId))
                    {
                        route = new ValourRoute
                        {
                            Type = ValourRouteType.PlanetWikiPage,
                            PlanetId = docsPlanet,
                            PageId = pageId,
                        };
                        return true;
                    }

                    route = new ValourRoute
                    {
                        Type = ValourRouteType.PlanetWiki,
                        PlanetId = docsPlanet,
                    };
                    return true;
                }
                break;

            case "friends":
                route = new ValourRoute { Type = ValourRouteType.Friends };
                return true;

            // /i/{code} — planet invite links
            case "i":
                if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    route = new ValourRoute
                    {
                        Type = ValourRouteType.Invite,
                        InviteCode = parts[1],
                    };
                    return true;
                }
                break;
        }

        return false;
    }

    /// <summary>
    /// Parses public docs URL segments starting at <paramref name="start"/>:
    /// {planetIdOrVanity} followed by an optional {pageSlug}. Vanity names are
    /// never all-digits, so a fully numeric first segment is always a planet id.
    /// </summary>
    private static bool TryParseWikiSegments(string[] parts, int start, out ValourRoute route)
    {
        route = default;

        if (parts.Length <= start)
            return false;

        var first = parts[start];

        long? planetId = null;
        string? vanity = null;

        if (long.TryParse(first, out var parsedPlanet))
            planetId = parsedPlanet;
        else if (IsWikiNameSegment(first))
            vanity = first.ToLowerInvariant();
        else
            return false;

        if (parts.Length > start + 1 && IsWikiNameSegment(parts[start + 1]))
        {
            route = new ValourRoute
            {
                Type = ValourRouteType.PlanetWikiPage,
                PlanetId = planetId,
                Vanity = vanity,
                PageSlug = parts[start + 1].ToLowerInvariant(),
            };
            return true;
        }

        route = new ValourRoute
        {
            Type = ValourRouteType.PlanetWiki,
            PlanetId = planetId,
            Vanity = vanity,
        };
        return true;
    }

    /// <summary>
    /// True for segments shaped like a docs slug or vanity name (letters,
    /// digits, dashes). Literal routes like "sitemap.xml" do not match.
    /// </summary>
    private static bool IsWikiNameSegment(string segment)
    {
        if (string.IsNullOrEmpty(segment) || segment.Length > 64)
            return false;

        foreach (var c in segment)
        {
            var lower = char.ToLowerInvariant(c);
            if (lower is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
                return false;
        }

        return true;
    }
}
