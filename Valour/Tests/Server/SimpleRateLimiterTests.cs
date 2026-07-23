using Valour.Server.Utilities;

namespace Valour.Tests.Server;

public class SimpleRateLimiterTests
{
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    [Fact]
    public void AllowsUpToLimit_ThenRejects()
    {
        var time = new FakeTimeProvider();
        var limiter = new SimpleRateLimiter(3, TimeSpan.FromMinutes(1), time);

        Assert.True(limiter.TryAcquire(1, out _));
        Assert.True(limiter.TryAcquire(1, out _));
        Assert.True(limiter.TryAcquire(1, out _));

        Assert.False(limiter.TryAcquire(1, out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
        Assert.True(retryAfter <= TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void WindowRollover_ResetsCount()
    {
        var time = new FakeTimeProvider();
        var limiter = new SimpleRateLimiter(2, TimeSpan.FromMinutes(1), time);

        Assert.True(limiter.TryAcquire(1, out _));
        Assert.True(limiter.TryAcquire(1, out _));
        Assert.False(limiter.TryAcquire(1, out _));

        time.Advance(TimeSpan.FromMinutes(1));

        Assert.True(limiter.TryAcquire(1, out _));
    }

    [Fact]
    public void Keys_AreIsolated()
    {
        var time = new FakeTimeProvider();
        var limiter = new SimpleRateLimiter(1, TimeSpan.FromMinutes(1), time);

        Assert.True(limiter.TryAcquire(1, out _));
        Assert.False(limiter.TryAcquire(1, out _));

        Assert.True(limiter.TryAcquire(2, out _));
    }

    [Fact]
    public void RetryAfter_ShrinksAsWindowAges()
    {
        var time = new FakeTimeProvider();
        var limiter = new SimpleRateLimiter(1, TimeSpan.FromMinutes(1), time);

        Assert.True(limiter.TryAcquire(1, out _));

        time.Advance(TimeSpan.FromSeconds(45));

        Assert.False(limiter.TryAcquire(1, out var retryAfter));
        Assert.Equal(15, retryAfter.TotalSeconds, precision: 0);
    }
}
