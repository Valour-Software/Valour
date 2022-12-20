using System.Collections.Concurrent;
using Valour.Server.Database;
using Valour.Server.Database.Items.Authorization;

namespace Valour.Server.Services;

public class TokenService
{
    private static readonly ConcurrentDictionary<string, AuthToken> QuickCache = new();
    
    private readonly ValourDB _db;
    private IHttpContextAccessor _contextAccessor;
    
    public TokenService(ValourDB db, IHttpContextAccessor contextAccessor)
    {
        _db = db;
        _contextAccessor = contextAccessor;
    }

    /// <summary>
    /// Will return the auth object for a valid token.
    /// A null response means the key was invalid.
    /// </summary>
    public async ValueTask<AuthToken> GetAsync(string key)
    {
        // If the key is empty or null, return null
        if (string.IsNullOrWhiteSpace(key))
            return null;

        // Try to get a cached auth token
        QuickCache.TryGetValue(key, out var token);
        
        if (token is null)
        {
            // If the auth token is null, try to get it from the database
            token = await _db.AuthTokens.FindAsync(key);
            
            // If there was a token, add it to the cache
            if (token is not null)
                QuickCache[key] = token;
        }

        return token;
    }
    
    public async ValueTask<AuthToken> GetCurrentToken()
    {
        _contextAccessor.HttpContext.Request.Headers.TryGetValue("authorization", out var authKey);
        return await GetAsync(authKey);
    }
}