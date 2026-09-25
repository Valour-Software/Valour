using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Valour.Server.Services;

/// <summary>
/// Keeps the static caches of the encryption services bounded. Every entry can
/// be rebuilt from the database, so a cache that grows past its limit drops a
/// tenth of its entries and refills with the ones still in use.
///
/// Counting a <see cref="ConcurrentDictionary{TKey,TValue}"/> takes all of its
/// locks, so the size is only checked every <see cref="CheckInterval"/>
/// insertions. A cache can therefore pass its limit by that many entries.
/// </summary>
internal static class E2eeCacheLimit
{
    private const int CheckInterval = 256;

    private sealed class Counter
    {
        public int Inserts;
    }

    private static readonly ConditionalWeakTable<object, Counter> Counters = new();

    /// <summary>Call before adding an entry to <paramref name="cache"/>.</summary>
    public static void Trim<TKey, TValue>(ConcurrentDictionary<TKey, TValue> cache, int maxEntries)
        where TKey : notnull
    {
        var counter = Counters.GetValue(cache, _ => new Counter());
        if (Interlocked.Increment(ref counter.Inserts) % CheckInterval != 0)
            return;

        if (cache.Count < maxEntries)
            return;

        // Enumerating the dictionary takes no locks, and its order follows the
        // hash buckets, so the removed tenth is effectively random.
        var remove = Math.Max(1, maxEntries / 10);
        foreach (var pair in cache)
        {
            cache.TryRemove(pair.Key, out _);
            if (--remove == 0)
                break;
        }
    }
}
