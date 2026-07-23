using System.Collections.Concurrent;

namespace Valour.Server.Utilities;

/// <summary>
/// A minimal fixed-window rate limiter keyed by a long id. In-process only;
/// sits behind whatever edge (Cloudflare) limiting already exists.
/// </summary>
public class SimpleRateLimiter
{
    private sealed class Window
    {
        public long WindowStartTicks;
        public int Count;
    }

    private readonly int _limit;
    private readonly TimeSpan _windowLength;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<long, Window> _windows = new();

    // Prune expired windows once the dictionary grows past this
    private const int PruneThreshold = 10_000;

    public SimpleRateLimiter(int limit, TimeSpan windowLength, TimeProvider time = null)
    {
        _limit = limit;
        _windowLength = windowLength;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Attempts to count one request for the key. Returns false when the
    /// key is over its limit, with the time until the window resets.
    /// </summary>
    public bool TryAcquire(long key, out TimeSpan retryAfter)
    {
        var now = _time.GetUtcNow().UtcTicks;
        var window = _windows.GetOrAdd(key, _ => new Window { WindowStartTicks = now });

        lock (window)
        {
            if (now - window.WindowStartTicks >= _windowLength.Ticks)
            {
                window.WindowStartTicks = now;
                window.Count = 0;
            }

            if (window.Count >= _limit)
            {
                retryAfter = TimeSpan.FromTicks(window.WindowStartTicks + _windowLength.Ticks - now);
                return false;
            }

            window.Count++;
        }

        retryAfter = TimeSpan.Zero;

        if (_windows.Count > PruneThreshold)
            Prune(now);

        return true;
    }

    private void Prune(long now)
    {
        foreach (var pair in _windows)
        {
            if (now - pair.Value.WindowStartTicks >= _windowLength.Ticks)
                _windows.TryRemove(pair.Key, out _);
        }
    }
}

/// <summary>
/// Rate limiter for webhook execution, keyed by webhook id.
/// Registered as a singleton.
/// </summary>
public sealed class WebhookRateLimiter : SimpleRateLimiter
{
    public const int RequestsPerMinute = 30;

    public WebhookRateLimiter() : base(RequestsPerMinute, TimeSpan.FromMinutes(1)) { }
}
