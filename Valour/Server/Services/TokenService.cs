using System.Collections.Concurrent;

namespace Valour.Server.Services;

public class TokenService
{
    private static readonly ConcurrentDictionary<string, AuthToken> QuickCache = new();
    
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
    /// Will return the auth object for a valid token.
    /// A null response means the key was invalid or expired.
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
            var dbToken = await _db.AuthTokens.FindAsync(key);
            if (dbToken is null)
                return null;
                
            token = dbToken.ToModel();
            
            // If there was a token, add it to the cache
            if (token is not null)
                QuickCache[key] = token;
        }

        // Check if token is expired
        if (token.TimeExpires < DateTime.UtcNow)
        {
            // Remove expired token from cache and database
            QuickCache.Remove(key, out _);
            var dbToken = await _db.AuthTokens.FindAsync(key);
            if (dbToken is not null)
            {
                _db.AuthTokens.Remove(dbToken);
                await _db.SaveChangesAsync();
            }
            return null;
        }

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