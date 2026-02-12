using System.Collections.Concurrent;
using Valour.Server.Exceptions;
using Valour.Server.Utilities;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public struct HostedPlanetResult
{
    public static readonly HostedPlanetResult DoesNotExist = new()
    {
        CorrectNode = "NONE",
        HostedPlanet = null
    };
    
    public HostedPlanet HostedPlanet;
    public string CorrectNode;

    // If found, return the planet
    public HostedPlanetResult(HostedPlanet hostedPlanet)
    {
        this.HostedPlanet = hostedPlanet;
        this.CorrectNode = null;
    }
    
    // If not found, return the correct node
    public HostedPlanetResult(string correctNode)
    {
        this.HostedPlanet = null;
        this.CorrectNode = correctNode;
    }
}

public class HostedPlanetService
{
    private readonly ValourDb _db;
    private readonly NodeLifecycleService _nodeLifecycleService;
    private readonly ModelCacheService _cache;
    private readonly ILogger<HostedPlanetService> _logger;

    public HostedPlanetService(ValourDb db, NodeLifecycleService nodeLifecycleService, ModelCacheService cache, ILogger<HostedPlanetService> logger)
    {
        _db = db;
        _nodeLifecycleService = nodeLifecycleService;
        _cache = cache;
        _logger = logger;
    }
    
    public async Task<HostedPlanet> BeginHosting(long planetId)
    {
        // If we're already hosting this planet, do nothing
        var cached = _cache.HostedPlanets.Get(planetId);
        if (cached is not null)
            return cached;
        
        var planet = (await _db.Planets.FindAsync(planetId)).ToModel();
        if (planet == null)
            return null;

        // Repair any broken channel positions before loading into cache
        await RepairChannelPositionsAsync(planetId);

        // Load data that should be cached (exclude soft-deleted channels)
        var channels = await _db.Channels
            .Where(c => c.PlanetId == planetId && !c.IsDeleted)
            .Select(c => c.ToModel())
            .ToListAsync();
        
        
        var roles = await _db.PlanetRoles
            .Where(r => r.PlanetId == planetId)
            .Select(r => r.ToModel())
            .ToListAsync();
        
        var hostedPlanet = new HostedPlanet(planet, channels, roles);
        
        _cache.HostedPlanets.Set(hostedPlanet);
        
        return hostedPlanet;
    }
    
    /// <summary>
    /// Returns the hosted planet if it is hosted on this node, or the correct node if it is not.
    /// </summary>
    public async ValueTask<HostedPlanetResult> TryGetAsync(long id)
    {
        var hostedPlanet = _cache.HostedPlanets.Get(id);
        if (hostedPlanet is not null)
            return new HostedPlanetResult(hostedPlanet);
        
        // Make sure the planet exists
        if (!await _db.Planets.AnyAsync(p => p.Id == id))
            return HostedPlanetResult.DoesNotExist;

        // Determine if the planet is hosted elsewhere
        var nodeName = await _nodeLifecycleService.GetActiveNodeForPlanetAsync(id);
        
        // If it's here, we should be hosting it
        if (nodeName == _nodeLifecycleService.Name)
        {
            return new HostedPlanetResult(await BeginHosting(id));
        }
        
        // Return where it should be hosted
        return new HostedPlanetResult(nodeName);
    }
    
    /// <summary>
    /// Returns the given hosted planet if hosted on this node.
    /// Otherwise, throws an exception which will be automatically handled by the API
    /// to redirect to the correct node. See: <see cref="NotHostedExceptionFilter"/>
    /// </summary>
    public async ValueTask<HostedPlanet> GetRequiredAsync(long id)
    {
        var result = await TryGetAsync(id);
        if (result.HostedPlanet is not null)
        {
            return result.HostedPlanet;
        }
        
        throw new PlanetNotHostedException(id, result.CorrectNode);
    }
    
    public void Remove(long id)
    {
        _cache.HostedPlanets.Remove(id);
    }
    
    public bool IsHosted(long id)
    {
        return _cache.HostedPlanets.ContainsKey(id);
    }
    
    public long[] GetHostedIds()
    {
        return _cache.HostedPlanets.Ids;
    }

    /// <summary>
    /// Detects and fixes broken channel layouts for a planet.
    /// Fixes orphaned channels, invalid parents, circular references, and rebuilds positions.
    /// </summary>
    private async Task RepairChannelPositionsAsync(long planetId)
    {
        var channels = await _db.Channels
            .Where(c => c.PlanetId == planetId && !c.IsDeleted)
            .ToListAsync();

        if (channels.Count == 0)
            return;

        var lookup = channels.ToDictionary(c => c.Id);
        var repairs = new List<string>();

        // Fix orphaned channels (ParentId points to non-existent channel)
        foreach (var channel in channels)
        {
            if (channel.ParentId is not null && !lookup.ContainsKey(channel.ParentId.Value))
            {
                repairs.Add($"Orphaned channel '{channel.Name}' ({channel.Id}): ParentId {channel.ParentId} does not exist, moved to root");
                channel.ParentId = null;
            }
        }

        // Fix channels whose parent is not a category
        foreach (var channel in channels)
        {
            if (channel.ParentId is not null &&
                lookup.TryGetValue(channel.ParentId.Value, out var parent) &&
                parent.ChannelType != ChannelTypeEnum.PlanetCategory)
            {
                repairs.Add($"Channel '{channel.Name}' ({channel.Id}): parent '{parent.Name}' ({parent.Id}) is not a category, moved to root");
                channel.ParentId = null;
            }
        }

        // Detect circular parent references
        foreach (var channel in channels)
        {
            if (channel.ParentId is null)
                continue;

            var visited = new HashSet<long> { channel.Id };
            var current = channel;

            while (current.ParentId is not null)
            {
                if (!lookup.TryGetValue(current.ParentId.Value, out var next))
                    break;

                if (!visited.Add(next.Id))
                {
                    // Cycle detected - break it by moving this channel to root
                    repairs.Add($"Circular reference detected for channel '{channel.Name}' ({channel.Id}), moved to root");
                    channel.ParentId = null;
                    break;
                }

                current = next;
            }
        }

        // Rebuild positions top-down (parents must be fixed before children)
        var childrenByParent = new Dictionary<long, List<Valour.Database.Channel>>();
        var rootChannels = new List<Valour.Database.Channel>();
        foreach (var group in channels.GroupBy(c => c.ParentId))
        {
            var ordered = group.OrderBy(c => c.RawPosition).ToList();
            if (group.Key is null)
                rootChannels = ordered;
            else
                childrenByParent[group.Key.Value] = ordered;
        }

        void RebuildPositions(long? parentId, uint parentPos)
        {
            List<Valour.Database.Channel> children;
            if (parentId is null)
                children = rootChannels;
            else if (!childrenByParent.TryGetValue(parentId.Value, out children))
                return;

            uint localPos = 1;
            foreach (var channel in children)
            {
                var newPos = ChannelPosition.AppendRelativePosition(parentPos, localPos);
                if (channel.RawPosition != newPos)
                {
                    repairs.Add($"Position fix for '{channel.Name}' ({channel.Id}): {channel.RawPosition} -> {newPos}");
                    channel.RawPosition = newPos;
                }

                // Recurse into children of this channel
                if (channel.ChannelType == ChannelTypeEnum.PlanetCategory)
                    RebuildPositions(channel.Id, channel.RawPosition);

                localPos++;
            }
        }

        RebuildPositions(null, 0);

        if (repairs.Count > 0)
        {
            _logger.LogWarning("Repaired {Count} channel position issues for planet {PlanetId}:\n{Details}",
                repairs.Count, planetId, string.Join("\n", repairs));

            _db.Channels.UpdateRange(channels);
            await _db.SaveChangesAsync();
        }
    }
}