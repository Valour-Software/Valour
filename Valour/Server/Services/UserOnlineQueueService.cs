using System.Collections.Concurrent;

namespace Valour.Server.Services;

public class UserOnlineQueueService
{
    private readonly ConcurrentDictionary<long, bool> _pendingUpdates = new();

    public void Enqueue(long userId, bool isMobile = false)
    {
        _pendingUpdates.AddOrUpdate(userId, isMobile, (_, existing) => existing || isMobile);
    }

    public List<(long UserId, bool IsMobile)> Drain(int maxItems)
    {
        if (maxItems <= 0 || _pendingUpdates.IsEmpty)
            return [];

        var results = new List<(long UserId, bool IsMobile)>(Math.Min(maxItems, _pendingUpdates.Count));

        foreach (var pair in _pendingUpdates)
        {
            if (results.Count >= maxItems)
                break;

            if (_pendingUpdates.TryRemove(pair.Key, out var isMobile))
                results.Add((pair.Key, isMobile));
        }

        return results;
    }

    public void Requeue(IEnumerable<(long UserId, bool IsMobile)> updates)
    {
        foreach (var (userId, isMobile) in updates)
        {
            Enqueue(userId, isMobile);
        }
    }
}
