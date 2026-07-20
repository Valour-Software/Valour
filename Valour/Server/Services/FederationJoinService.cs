using Valour.Database;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Hub-side join flow for community-hosted planets. The hub is ground truth for
/// who belongs where: joins are recorded here before the node sees the user (so
/// a node can't fabricate memberships), and the accepted-domains list backs the
/// first-contact warning and syncs across the user's devices.
/// </summary>
public class FederationJoinService
{
    private readonly ValourDb _db;

    public FederationJoinService(ValourDb db)
    {
        _db = db;
    }

    // ---- Accepted domains (opt-in warning list) ----

    public async Task<TaskResult> AcceptDomainAsync(long userId, string domain)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult.FromFailure("Community-server features are only available on the official server.");

        domain = FederationHubService.NormalizeDomain(domain);
        if (domain is null)
            return TaskResult.FromFailure("Invalid domain.");

        // Consent is meaningful only for a currently active community node.
        // Do not let an account accumulate arbitrary DNS names merely because
        // they happen to parse as domains; that creates unbounded junk state
        // and would make the accepted-domains list look like an address book.
        var nodeActive = await _db.FederatedNodes.AsNoTracking()
            .AnyAsync(x => x.Domain == domain && x.Status == FederatedNodeStatus.Active);
        if (!nodeActive)
            return TaskResult.FromFailure("Domain is not an active community node.");

        if (!await _db.FederatedAcceptedDomains.AnyAsync(x => x.UserId == userId && x.Domain == domain))
        {
            await _db.FederatedAcceptedDomains.AddAsync(new FederatedAcceptedDomain
            {
                UserId = userId,
                Domain = domain,
                AcceptedAt = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync();
        }

        return TaskResult.SuccessResult;
    }

    public async Task<List<string>> GetAcceptedDomainsAsync(long userId)
    {
        if (!FederationHubService.HubEnabled)
            return new List<string>();

        return await _db.FederatedAcceptedDomains.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.Domain)
            .ToListAsync();
    }

    public Task<bool> IsDomainAcceptedAsync(long userId, string domain) =>
        FederationHubService.HubEnabled
            ? _db.FederatedAcceptedDomains.AsNoTracking()
                .AnyAsync(x => x.UserId == userId && x.Domain == domain)
            : Task.FromResult(false);

    // ---- Planet host resolution + join ----

    /// <summary>
    /// Where a community-hosted planet lives, or null if it isn't a federated
    /// stub (i.e. it's official, resolved through the normal node path).
    /// </summary>
    public async Task<FederatedPlanetLocation> ResolveAsync(long userId, long planetId)
    {
        if (!FederationHubService.HubEnabled)
            return null;

        var stub = await _db.FederatedPlanetStubs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == planetId);
        if (stub is null)
            return null;

        // Private or unlisted planets are addressable only by people who
        // already have a hub-side membership (for example, members carried
        // across a migration). Knowing a snowflake must not expose their host
        // or create a route into the node.
        if ((!stub.Public || !stub.Discoverable) &&
            !await _db.FederatedMemberships.AsNoTracking()
                .AnyAsync(x => x.UserId == userId && x.PlanetId == planetId && x.NodeDomain == stub.NodeDomain))
        {
            return null;
        }

        var node = await _db.FederatedNodes.AsNoTracking().FirstOrDefaultAsync(x => x.Domain == stub.NodeDomain);
        if (node is null || node.Status != FederatedNodeStatus.Active)
            return null;

        return new FederatedPlanetLocation
        {
            PlanetId = stub.Id,
            NodeDomain = stub.NodeDomain,
            Name = stub.Name,
            DomainAccepted = await IsDomainAcceptedAsync(userId, stub.NodeDomain),
        };
    }

    /// <summary>
    /// Records a user's join of a community planet. Requires the domain to be
    /// accepted (the client shows the warning first). Returns where to connect.
    /// </summary>
    public async Task<TaskResult<FederatedPlanetLocation>> JoinAsync(long userId, long planetId)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult<FederatedPlanetLocation>.FromFailure("Community-server features are only available on the official server.");

        var stub = await _db.FederatedPlanetStubs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == planetId);
        if (stub is null)
            return TaskResult<FederatedPlanetLocation>.FromFailure("Not a community-hosted planet, or its node is unavailable.");

        var alreadyMember = await _db.FederatedMemberships.AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.PlanetId == planetId && x.NodeDomain == stub.NodeDomain);

        // Private federation invites are redeemed through the recipient-bound
        // grant flow on the destination node. Keep this unauthenticated
        // self-service route fail-closed: only public, discoverable planets
        // may create a new hub membership here.
        if (!alreadyMember && (!stub.Public || !stub.Discoverable))
            return TaskResult<FederatedPlanetLocation>.FromFailure("This community requires an invitation from its host.");

        var location = await ResolveAsync(userId, planetId);
        if (location is null)
            return TaskResult<FederatedPlanetLocation>.FromFailure("Not a community-hosted planet, or its node is unavailable.");

        if (!location.DomainAccepted)
            return TaskResult<FederatedPlanetLocation>.FromFailure("Accept the node's domain before joining.");

        if (!alreadyMember)
        {
            await _db.FederatedMemberships.AddAsync(new FederatedMembership
            {
                UserId = userId,
                PlanetId = planetId,
                NodeDomain = location.NodeDomain,
                JoinedAt = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync();
        }

        return TaskResult<FederatedPlanetLocation>.FromData(location);
    }

    public async Task<TaskResult> LeaveAsync(long userId, long planetId)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult.FromFailure("Community-server features are only available on the official server.");

        var membership = await _db.FederatedMemberships
            .FirstOrDefaultAsync(x => x.UserId == userId && x.PlanetId == planetId);
        if (membership is not null)
        {
            _db.FederatedMemberships.Remove(membership);
            await _db.SaveChangesAsync();
        }
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// The user's community-hosted memberships ("your planets on other servers").
    /// </summary>
    public async Task<List<FederatedMembershipInfo>> GetMembershipsAsync(long userId)
    {
        if (!FederationHubService.HubEnabled)
            return new List<FederatedMembershipInfo>();

        return await (from membership in _db.FederatedMemberships.AsNoTracking()
               join stub in _db.FederatedPlanetStubs.AsNoTracking()
                   on membership.PlanetId equals stub.Id
               where membership.UserId == userId && membership.NodeDomain == stub.NodeDomain
               select new FederatedMembershipInfo
            {
                PlanetId = membership.PlanetId,
                NodeDomain = membership.NodeDomain,
                JoinedAt = membership.JoinedAt,
            })
            .ToListAsync();
    }
}
