using System.Security.Cryptography;
using System.Text.Json;
using StackExchange.Redis;
using Valour.Server.Redis;

namespace Valour.Server.Services;

/// <summary>
/// Short-lived sign-in state kept in Redis, so every node can finish a flow
/// that another node started: OAuth state, sign-in tickets, registration
/// tickets, identity proofs, and device key challenges. Each value is stored
/// under a random ID that is only ever given to the client that asked for it.
/// </summary>
public class AuthTicketStore
{
    private readonly IConnectionMultiplexer _redis;

    public AuthTicketStore(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    private IDatabase Db => _redis.GetDatabase(RedisDbTypes.Cluster);

    private static string Key(string kind, string id) => $"auth:ticket:{kind}:{id}";

    /// <summary>Stores a value and returns its new random ID.</summary>
    public async Task<string> CreateAsync<T>(string kind, T value, TimeSpan lifetime)
    {
        var id = NewId();
        await Db.StringSetAsync(Key(kind, id), JsonSerializer.Serialize(value), lifetime);
        return id;
    }

    /// <summary>Stores a value under an ID the caller already has.</summary>
    public Task SetAsync<T>(string kind, string id, T value, TimeSpan lifetime) =>
        IsValidId(id)
            ? Db.StringSetAsync(Key(kind, id), JsonSerializer.Serialize(value), lifetime)
            : Task.CompletedTask;

    /// <summary>Reads a value without removing it. Returns default when missing or expired.</summary>
    public async Task<T> GetAsync<T>(string kind, string id)
    {
        if (!IsValidId(id))
            return default;

        var value = await Db.StringGetAsync(Key(kind, id));
        return value.HasValue ? JsonSerializer.Deserialize<T>(value.ToString()) : default;
    }

    /// <summary>Reads and removes a value in one step, so it can be used only once.</summary>
    public async Task<T> TakeAsync<T>(string kind, string id)
    {
        if (!IsValidId(id))
            return default;

        var value = await Db.StringGetDeleteAsync(Key(kind, id));
        return value.HasValue ? JsonSerializer.Deserialize<T>(value.ToString()) : default;
    }

    public Task RemoveAsync(string kind, string id) =>
        IsValidId(id) ? Db.KeyDeleteAsync(Key(kind, id)) : Task.CompletedTask;

    /// <summary>A random URL-safe ID with 256 bits of entropy.</summary>
    public static string NewId() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// The base64url SHA-256 of a client verifier, as clients send it when
    /// they start a flow.
    /// </summary>
    public static string HashVerifier(string verifier) =>
        Base64Url(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(verifier ?? string.Empty)));

    /// <summary>Checks a client's verifier against the hash it sent earlier, in constant time.</summary>
    public static bool VerifierMatches(string verifier, string expectedHash)
    {
        if (string.IsNullOrEmpty(verifier) || string.IsNullOrEmpty(expectedHash))
            return false;

        var actual = System.Text.Encoding.UTF8.GetBytes(HashVerifier(verifier));
        var expected = System.Text.Encoding.UTF8.GetBytes(expectedHash);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool IsValidId(string id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
