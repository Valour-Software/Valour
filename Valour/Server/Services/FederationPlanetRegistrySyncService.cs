using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Reconciles hub discovery stubs with the node's database-authoritative
/// planet metadata. Membership changes occur through many paths, so a periodic
/// outbox-style reconciliation is safer than relying on each path to remember
/// an S2S update.
/// </summary>
public class FederationPlanetRegistrySyncService
{
    private readonly ValourDb _db;
    private readonly FederationNodeClient _nodeClient;
    private readonly ILogger<FederationPlanetRegistrySyncService> _logger;

    public FederationPlanetRegistrySyncService(
        ValourDb db,
        FederationNodeClient nodeClient,
        ILogger<FederationPlanetRegistrySyncService> logger)
    {
        _db = db;
        _nodeClient = nodeClient;
        _logger = logger;
    }

    public async Task<int> SyncAllAsync()
    {
        if (!FederationNodeService.NodeEnabled)
            return 0;

        var planets = await _db.Planets.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => new
            {
                x.Id, x.Name, x.Description, x.OwnerId, x.Nsfw, x.Public, x.Discoverable,
            })
            .ToListAsync();

        var synced = 0;
        foreach (var planet in planets)
        {
            var memberCount = await _db.PlanetMembers.CountAsync(x => x.PlanetId == planet.Id);
            var result = await _nodeClient.UpsertPlanetAsync(planet.Id, new FederatedPlanetStubRequest
            {
                Id = planet.Id,
                Name = planet.Name,
                Description = planet.Description,
                OwnerId = planet.OwnerId,
                MemberCount = memberCount,
                Nsfw = planet.Nsfw,
                Public = planet.Public,
                Discoverable = planet.Discoverable,
            });

            if (result.Success)
                synced++;
            else
                _logger.LogWarning("Could not reconcile federation stub for planet {PlanetId}: {Message}",
                    planet.Id, result.Message);
        }

        return synced;
    }
}
