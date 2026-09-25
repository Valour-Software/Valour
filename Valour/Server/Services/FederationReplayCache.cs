using StackExchange.Redis;
using Valour.Server.Redis;

namespace Valour.Server.Services;

/// <summary>
/// Remembers the unique id (jti) of a single-use federation credential until
/// the credential expires. The record lives in the cluster's shared Redis, so
/// every replica of a hub or community node rejects a second presentation of
/// the same credential.
/// </summary>
public static class FederationReplayCache
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Records the credential id and returns true the first time it is seen.
    /// Returns false for a replay. The caller must have validated the
    /// credential's signature and lifetime first, so the stored key is bounded
    /// by an expiry the caller already accepted.
    /// </summary>
    public static async Task<bool> TryConsumeAsync(
        IConnectionMultiplexer redis,
        string purpose,
        string issuer,
        string credentialId,
        DateTime expiresUtc)
    {
        var lifetime = expiresUtc - DateTime.UtcNow + ClockSkew;
        if (lifetime <= TimeSpan.Zero)
            lifetime = ClockSkew;

        var key = $"federation:jti:{purpose}:{issuer}:{credentialId}";
        return await redis.GetDatabase(RedisDbTypes.Cluster)
            .StringSetAsync(key, 1, lifetime, When.NotExists);
    }
}
