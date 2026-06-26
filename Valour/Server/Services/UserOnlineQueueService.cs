using System.Collections.Concurrent;

namespace Valour.Server.Services;

public readonly record struct UserOnlineUpdate(long UserId, bool IsMobile, long[] PlanetIds);

public class UserOnlineQueueService
{
    private readonly ConcurrentDictionary<long, PendingUserOnlineUpdate> _pendingUpdates = new();

    public void Enqueue(long userId, bool isMobile = false, IEnumerable<long> planetIds = null)
    {
        var normalizedPlanetIds = planetIds?
            .Where(x => x > 0)
            .Distinct()
            .ToArray() ?? [];

        _pendingUpdates.AddOrUpdate(
            userId,
            _ => new PendingUserOnlineUpdate(isMobile, normalizedPlanetIds),
            (_, existing) => existing.Merge(isMobile, normalizedPlanetIds));
    }

    public List<UserOnlineUpdate> Drain(int maxItems)
    {
        if (maxItems <= 0 || _pendingUpdates.IsEmpty)
            return [];

        var results = new List<UserOnlineUpdate>(Math.Min(maxItems, _pendingUpdates.Count));

        foreach (var pair in _pendingUpdates)
        {
            if (results.Count >= maxItems)
                break;

            if (_pendingUpdates.TryRemove(pair.Key, out var update))
                results.Add(new UserOnlineUpdate(pair.Key, update.IsMobile, update.PlanetIds));
        }

        return results;
    }

    public void Requeue(IEnumerable<UserOnlineUpdate> updates)
    {
        foreach (var update in updates)
        {
            Enqueue(update.UserId, update.IsMobile, update.PlanetIds);
        }
    }

    private readonly record struct PendingUserOnlineUpdate(bool IsMobile, long[] PlanetIds)
    {
        public PendingUserOnlineUpdate Merge(bool isMobile, long[] planetIds)
        {
            if (planetIds.Length == 0)
                return this with { IsMobile = IsMobile || isMobile };

            if (PlanetIds.Length == 0)
                return new PendingUserOnlineUpdate(IsMobile || isMobile, planetIds);

            return new PendingUserOnlineUpdate(
                IsMobile || isMobile,
                PlanetIds.Concat(planetIds).Distinct().ToArray());
        }
    }
}
