using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;
using Valour.Server.Redis;

namespace Valour.Server.Utilities;

/// <summary>
/// Counts failed credential checks per account in Redis so that guessing is
/// bounded per target, not only per client address. IP rate limits alone do
/// not stop a distributed guess against one account.
///
/// Counters live in Redis so every node shares them. Redis errors fail open:
/// an outage should not lock every user out, and the IP limits still apply.
/// </summary>
public static class AuthAttemptThrottle
{
    /// <summary>
    /// Failed password attempts allowed per account from one client address in
    /// a window. Other addresses are unaffected, so a stranger cannot lock the
    /// owner out cheaply.
    /// </summary>
    public const int ClientPasswordFailureLimit = 10;

    /// <summary>
    /// Failed password attempts allowed per account across all addresses in a
    /// window. This bounds guessing spread over many addresses.
    /// </summary>
    public const int AccountPasswordFailureLimit = 100;

    /// <summary>Failed authenticator codes allowed per account in a window.</summary>
    public const int MultiFactorFailureLimit = 5;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public const string LockedMessage =
        "Too many failed attempts for this account. Please wait 15 minutes and try again.";

    /// <summary>
    /// Password failures are keyed by the submitted identifier, so the result
    /// is the same whether or not the account exists. The identifier is hashed
    /// to keep email addresses out of Redis.
    /// </summary>
    public static string PasswordKey(string identifier) =>
        "auth:pw-fail:" + Hash(identifier.Trim().ToLowerInvariant());

    /// <summary>
    /// Password failures for one identifier from one client, where
    /// <paramref name="clientKey"/> is the rate-limit key of the caller's address.
    /// </summary>
    public static string PasswordKey(string identifier, string clientKey) =>
        "auth:pw-fail-client:" + Hash(identifier.Trim().ToLowerInvariant() + "|" + clientKey);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string MultiFactorKey(long userId) => $"auth:mfa-fail:{userId}";

    public static async Task<bool> IsLockedAsync(IConnectionMultiplexer redis, string key, int limit, ILogger logger)
    {
        try
        {
            var value = await redis.GetDatabase(RedisDbTypes.Cluster).StringGetAsync(key);
            return value.TryParse(out long count) && count >= limit;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Auth throttle lookup failed; allowing the attempt.");
            return false;
        }
    }

    public static async Task RecordFailureAsync(IConnectionMultiplexer redis, string key, ILogger logger)
    {
        try
        {
            var db = redis.GetDatabase(RedisDbTypes.Cluster);
            var count = await db.StringIncrementAsync(key);

            // The window starts at the first failure and is not extended by later
            // ones. The TTL check repairs a counter whose expiry was never set.
            if (count == 1 || await db.KeyTimeToLiveAsync(key) is null)
                await db.KeyExpireAsync(key, Window);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to record an auth failure.");
        }
    }

    public static async Task ResetAsync(IConnectionMultiplexer redis, string key, ILogger logger)
    {
        try
        {
            await redis.GetDatabase(RedisDbTypes.Cluster).KeyDeleteAsync(key);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to reset an auth failure counter.");
        }
    }
}
