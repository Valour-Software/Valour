using System.Collections.Concurrent;
using User = Valour.Server.Models.User;

namespace Valour.Server.Services;

/// <summary>
/// A node-global cache of <see cref="User"/> models. Users rarely change, so caching them
/// lets planet-scoped reads (member resolution, member lists, notifications) compose user
/// data in memory instead of joining the users table on every request.
///
/// Entries are written through whenever a user changes on this node and carry a short TTL so
/// that cross-node edits (a user updated on their primary node while cached on a planet node)
/// converge without an explicit invalidation bus. The <see cref="HostedPlanetCleanupWorker"/>
/// periodically evicts users that are no longer referenced by any planet hosted on this node.
/// </summary>
public class UserCacheService
{
    /// <summary>
    /// How long a cached user is trusted before it is re-fetched on the next read. User data is
    /// cosmetic (name, avatar, status) so a small staleness window is acceptable.
    /// </summary>
    private static readonly long TtlTicks = TimeSpan.FromMinutes(5).Ticks;

    private sealed class Entry
    {
        public User User;
        public long CachedAtTicks;
    }

    private readonly ConcurrentDictionary<long, Entry> _cache = new();

    /// <summary>
    /// Returns the cached user if present and still within its TTL.
    /// </summary>
    public bool TryGet(long userId, out User user)
    {
        if (_cache.TryGetValue(userId, out var entry) &&
            DateTime.UtcNow.Ticks - entry.CachedAtTicks <= TtlTicks)
        {
            user = entry.User;
            return true;
        }

        user = null;
        return false;
    }

    /// <summary>
    /// Write-through: store the latest copy of a user, resetting its TTL.
    /// </summary>
    public void Set(User user)
    {
        if (user is null)
            return;

        _cache[user.Id] = new Entry
        {
            User = user,
            CachedAtTicks = DateTime.UtcNow.Ticks
        };
    }

    public void Remove(long userId) =>
        _cache.TryRemove(userId, out _);

    public long[] CachedIds => _cache.Keys.ToArray();

    public int Count => _cache.Count;

    /// <summary>
    /// Removes every cached user whose id is not in the provided keep set.
    /// </summary>
    public void Sweep(HashSet<long> keep)
    {
        foreach (var id in _cache.Keys)
        {
            if (!keep.Contains(id))
                _cache.TryRemove(id, out _);
        }
    }
}
