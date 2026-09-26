using System.Collections.Concurrent;

namespace Valour.Server.Services;

public class TokenService
{
    private readonly record struct CachedToken(AuthToken Token, DateTime TimeCached);

    private static readonly ConcurrentDictionary<string, CachedToken> QuickCache = new();

    /// <summary>
    /// How long a cached token is trusted before the database is checked again.
    /// Revocation evicts the token on the node that handled it; this TTL bounds
    /// how long other nodes keep accepting it.
    /// </summary>
    private static readonly TimeSpan QuickCacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long a Valour app session lasts without being used. Each use moves
    /// the expiry to this long from now, up to <see cref="MaxSessionAge"/>.
    /// </summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// Sessions end this long after sign-in even when used every day, so a
    /// stolen session can't be kept alive forever.
    /// </summary>
    public static readonly TimeSpan MaxSessionAge = TimeSpan.FromDays(90);

    /// <summary>
    /// A session's expiry is moved at most this often, so renewal costs one
    /// database write per session per hour instead of one per request.
    /// </summary>
    private static readonly TimeSpan SessionRenewInterval = TimeSpan.FromHours(1);

    /// <summary>The app ID of sessions from signing in to Valour itself.</summary>
    public const string SessionAppId = "VALOUR";
    
    private readonly ValourDb _db;
    private readonly IHttpContextAccessor _contextAccessor;

    /// <summary>
    /// Stores the current token if it has already been grabbed in this context
    /// </summary>
    private AuthToken _currentToken;
    
    public TokenService(ValourDb db, IHttpContextAccessor contextAccessor)
    {
        _db = db;
        _contextAccessor = contextAccessor;
    }

    public void RemoveFromQuickCache(string id)
    {
        QuickCache.Remove(id, out _);
    }

    /// <summary>
    /// Periodically evicts expired and stale tokens from the cache. Without this, tokens
    /// that are never presented again would stay resident for the process lifetime.
    /// </summary>
    public static void StartCacheSweepTask()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(10));

                    var now = DateTime.UtcNow;
                    var expired = QuickCache
                        .Where(kvp => kvp.Value.Token.TimeExpires < now ||
                                      now - kvp.Value.TimeCached >= QuickCacheTtl)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in expired)
                        QuickCache.TryRemove(key, out _);

                    if (expired.Count > 0)
                        Console.WriteLine($"Cleaned up {expired.Count} expired auth tokens from cache");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sweeping auth token cache: {ex.Message}");
                }
            }
        });
    }

    /// <summary>
    /// Will return the auth object for a valid token.
    /// A null response means the key was invalid or expired.
    /// </summary>
    public async ValueTask<AuthToken> GetAsync(string key)
    {
        // If the key is empty or null, return null
        if (string.IsNullOrWhiteSpace(key))
            return null;

        // Try to get a cached auth token that is still fresh
        AuthToken token = null;
        if (QuickCache.TryGetValue(key, out var cached) &&
            DateTime.UtcNow - cached.TimeCached < QuickCacheTtl)
        {
            token = cached.Token;
        }

        if (token is null)
        {
            // Not cached or stale: the database is the source of truth, so a
            // token revoked on another node stops working here as well
            var dbToken = await _db.AuthTokens.AsNoTracking().FirstOrDefaultAsync(x => x.Id == key);
            if (dbToken is null)
            {
                QuickCache.Remove(key, out _);
                return null;
            }

            token = dbToken.ToModel();
            QuickCache[key] = new CachedToken(token, DateTime.UtcNow);
        }

        // Check if token is expired
        if (token.TimeExpires < DateTime.UtcNow)
        {
            // Remove expired token from cache and database
            QuickCache.Remove(key, out _);
            
            // Use ExecuteDeleteAsync instead of Remove+SaveChangesAsync to avoid
            // DbUpdateConcurrencyException when multiple requests try to expire
            // the same token concurrently.
            await _db.AuthTokens.IgnoreQueryFilters()
                .Where(x => x.Id == key)
                .ExecuteDeleteAsync();
            
            return null;
        }

        return await RenewSessionAsync(token);
    }

    /// <summary>
    /// Restarts a Valour app session's lifetime when it is used. Tokens for
    /// bots, OAuth apps, and federation keep their fixed expiry, and an expiry
    /// is never moved earlier.
    /// </summary>
    private async ValueTask<AuthToken> RenewSessionAsync(AuthToken token)
    {
        if (token.AppId != SessionAppId)
            return token;

        var now = DateTime.UtcNow;
        var renewed = now + SessionLifetime;
        var latest = token.TimeCreated + MaxSessionAge;
        if (renewed > latest)
            renewed = latest;

        if (renewed - token.TimeExpires < SessionRenewInterval)
            return token;

        // Nothing changes when the session was revoked in the meantime, and
        // then it must not be cached again as if it were valid.
        var updated = await _db.AuthTokens.IgnoreQueryFilters()
            .Where(x => x.Id == token.Id && x.TimeExpires < renewed)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.TimeExpires, renewed));
        if (updated == 0)
            return token;

        token.TimeExpires = renewed;
        QuickCache[token.Id] = new CachedToken(token, now);
        return token;
    }

    public string GetAuthKey()
    {
        _contextAccessor.HttpContext!.Request.Headers.TryGetValue("authorization", out var authKey);
        return authKey;
    }
    
    public async ValueTask<AuthToken> GetCurrentTokenAsync()
    {
        if (_currentToken is not null)
            return _currentToken;
        
        var authKey = GetAuthKey();
        _currentToken = await GetAsync(authKey);
        return _currentToken;
    }
}