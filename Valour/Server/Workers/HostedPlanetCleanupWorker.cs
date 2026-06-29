using Valour.Server.Hubs;
using Valour.Server.Services;

namespace Valour.Server.Workers;

/// <summary>
/// Periodically reclaims memory for planets hosted on this node:
///   1. Unloads a hosted planet once its realtime group has been empty (no connected members and
///      no active voice participants) for a grace period. The planet simply re-hosts on the next
///      access, so dropping the local cache is safe.
///   2. Sweeps the node-global user cache, evicting users that are neither connected nor a member
///      of any planet still hosted on this node.
/// </summary>
public class HostedPlanetCleanupWorker : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long a planet's realtime group must remain empty before the planet is unloaded.
    /// </summary>
    private static readonly TimeSpan EmptyUnloadGracePeriod = TimeSpan.FromMinutes(5);

    private readonly ILogger<HostedPlanetCleanupWorker> _logger;
    private readonly ModelCacheService _modelCache;
    private readonly SignalRConnectionService _connectionTracker;
    private readonly UserCacheService _userCache;

    // Tracks when each hosted planet's group was first observed empty.
    private readonly Dictionary<long, DateTime> _emptySince = new();

    public HostedPlanetCleanupWorker(
        ILogger<HostedPlanetCleanupWorker> logger,
        ModelCacheService modelCache,
        SignalRConnectionService connectionTracker,
        UserCacheService userCache)
    {
        _logger = logger;
        _modelCache = modelCache;
        _connectionTracker = connectionTracker;
        _userCache = userCache;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
                UnloadIdlePlanets();
                SweepUserCache();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in hosted planet cleanup worker");
            }
        }
    }

    private void UnloadIdlePlanets()
    {
        var now = DateTime.UtcNow;
        var hostedIds = _modelCache.HostedPlanets.Ids;

        foreach (var planetId in hostedIds)
        {
            var hostedPlanet = _modelCache.HostedPlanets.Get(planetId);
            if (hostedPlanet is null)
            {
                _emptySince.Remove(planetId);
                continue;
            }

            var hasConnections = _connectionTracker.GetGroupConnections($"p-{planetId}").Length > 0;
            var hasVoice = hostedPlanet.HasActiveVoiceParticipants();

            if (hasConnections || hasVoice)
            {
                // Still in use - reset the empty timer.
                _emptySince.Remove(planetId);
                continue;
            }

            if (!_emptySince.TryGetValue(planetId, out var since))
            {
                _emptySince[planetId] = now;
                continue;
            }

            if (now - since < EmptyUnloadGracePeriod)
                continue;

            _modelCache.HostedPlanets.Remove(planetId);
            _emptySince.Remove(planetId);

            _logger.LogInformation(
                "Unloaded idle hosted planet {PlanetId} after {Minutes} minutes with no connections or voice participants",
                planetId, EmptyUnloadGracePeriod.TotalMinutes);
        }

        // Drop empty-timers for planets that are no longer hosted here at all.
        if (_emptySince.Count > 0)
        {
            var hostedSet = new HashSet<long>(hostedIds);
            var stale = _emptySince.Keys.Where(id => !hostedSet.Contains(id)).ToList();
            foreach (var id in stale)
                _emptySince.Remove(id);
        }
    }

    private void SweepUserCache()
    {
        var cachedIds = _userCache.CachedIds;
        if (cachedIds.Length == 0)
            return;

        // Keep every currently-connected user.
        var keep = new HashSet<long>(_connectionTracker.GetAllConnectedUserIds());

        var hostedPlanets = _modelCache.HostedPlanets.Ids
            .Select(id => _modelCache.HostedPlanets.Get(id))
            .Where(p => p is not null)
            .ToList();

        // Keep any cached user that is still a member of a planet hosted on this node. Iterating
        // the (small) set of cached users keeps this cheap regardless of total membership counts.
        foreach (var userId in cachedIds)
        {
            if (keep.Contains(userId))
                continue;

            foreach (var planet in hostedPlanets)
            {
                if (planet.TryGetMemberByUser(userId, out _))
                {
                    keep.Add(userId);
                    break;
                }
            }
        }

        _userCache.Sweep(keep);
    }
}
