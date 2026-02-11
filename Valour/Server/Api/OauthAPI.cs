using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using Valour.Shared.Models;
using Valour.Server.Database;
using System.Collections.Concurrent;
using Valour.Config.Configs;
using Valour.Shared.Authorization;

namespace Valour.Server.API;

/// <summary>
/// Cached OAuth authorization code with expiration tracking
/// </summary>
public class CachedOAuthCode
{
    public AuthorizeModel Model { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// OAuth codes expire after 10 minutes (per RFC 6749 recommendation)
    /// </summary>
    public bool IsExpired => DateTime.UtcNow - CreatedAt > TimeSpan.FromMinutes(10);
}

public class OauthAPI : BaseAPI
{
    /// <summary>
    /// Adds the routes for this API section
    /// </summary>
    public new static void AddRoutes(WebApplication app)
    {
        app.MapPost("api/oauth/app", CreateApp);
        app.MapGet("api/oauth/app/{app_id}", GetApp);
        app.MapDelete("api/oauth/app/{app_id}", DeleteApp);
        app.MapGet("api/oauth/app/public/{app_id}", GetAppPublic);

        app.MapGet("api/users/apps", GetApps);

        app.MapPost("api/oauth/authorize", Authorize);
        app.MapGet("api/oauth/token", Token);

        // Start background cleanup task for expired OAuth codes
        _ = Task.Run(CleanupExpiredCodesAsync);
    }

    /// <summary>
    /// Cache for OAuth authorization codes with expiration tracking
    /// </summary>
    public static ConcurrentDictionary<string, CachedOAuthCode> OauthReqCache = new();

    /// <summary>
    /// Background task to periodically clean up expired OAuth codes
    /// </summary>
    private static async Task CleanupExpiredCodesAsync()
    {
        while (true)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5));

                var expiredCodes = OauthReqCache
                    .Where(kvp => kvp.Value.IsExpired)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var code in expiredCodes)
                {
                    OauthReqCache.TryRemove(code, out _);
                }

                if (expiredCodes.Count > 0)
                {
                    Console.WriteLine($"Cleaned up {expiredCodes.Count} expired OAuth codes");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cleaning up OAuth codes: {ex.Message}");
            }
        }
    }

    public static async Task<object> Authorize(
        ValourDb db, HttpContext context,
        TokenService tokenService,
        [FromBody] AuthorizeModel model,
        [FromHeader] string authorization)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        if (authToken is null || model.UserId != authToken.UserId)
            return ValourResult.InvalidToken();

        var client = await db.OauthApps.FindAsync(model.ClientId);
        if (client is null)
            return ValourResult.NotFound($"App with id {model.ClientId} not found");

        if (client.RedirectUrl != model.RedirectUri)
            return ValourResult.Problem("Client redirect url does not match given url");

        model.Code = Guid.NewGuid().ToString();

        // Store code with creation timestamp for expiration tracking
        var cachedCode = new CachedOAuthCode
        {
            Model = model,
            CreatedAt = DateTime.UtcNow
        };
        OauthReqCache.TryAdd(model.Code, cachedCode);

        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");

        // Slightly different than normal oauth because of Blazor: We return the link for the app to redirect itself to
        return ValourResult.Ok($"{model.RedirectUri}?code={model.Code}&state={model.State}&node={NodeConfig.Instance.Name}");
    }

    public static async Task<IResult> Token(
        ValourDb db,
        long client_id,
        string client_secret,
        string grant_type,
        string code,
        string redirect_uri,
        string state
    )
    {
        OauthReqCache.TryGetValue(code, out var cached);
        if (cached is null)
            return ValourResult.Forbid("Invalid or expired authorization code.");

        // Check if the code has expired (10 minute lifetime per RFC 6749)
        if (cached.IsExpired)
        {
            OauthReqCache.TryRemove(code, out _);
            return ValourResult.Forbid("Authorization code has expired. Please re-authorize.");
        }

        var model = cached.Model;
        if (model.ClientId != client_id ||
            model.RedirectUri != redirect_uri ||
            model.State != state)
            return ValourResult.Forbid("Parameters are invalid.");

        var app = await db.OauthApps.FindAsync(client_id);
        if (app.Secret != client_secret)
            return ValourResult.Forbid("Parameters are invalid.");

        // Remove the code from cache - codes are single-use per RFC 6749
        OauthReqCache.TryRemove(code, out _);

        switch (grant_type)
        {
            case "authorization_code":
                {
                    AuthToken newToken = new AuthToken()
                    {
                        Id = "val-" + Guid.NewGuid().ToString(),
                        AppId = client_id.ToString(),
                        Scope = model.Scope,
                        TimeCreated = DateTime.UtcNow,
                        TimeExpires = DateTime.UtcNow.AddDays(7),
                        UserId = model.UserId,
                        IssuedAddress = "Oauth Internal"
                    };

                    await db.AuthTokens.AddAsync(newToken.ToDatabase());
                    await db.SaveChangesAsync();

                    return Results.Json(newToken);
                }

            default:
                return ValourResult.Problem("Available grant types: authorization_code");
        }

    }

    public static async Task DeleteApp(HttpContext context, ValourDb db, ulong app_id, TokenService tokenService, [FromHeader] string authorization)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();

        if (authToken is null)
        {
            await Unauthorized("Include token", context);
            return;
        }

        var app = await db.OauthApps.FindAsync(app_id);

        if (app.OwnerId != authToken.UserId)
        {
            await Unauthorized("You do not own this app!", context);
            return;
        }

        db.Remove(app);
        await db.SaveChangesAsync();
    }

    public static async Task GetApps(HttpContext context, ValourDb db, TokenService tokenService, [FromHeader] string authorization)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();

        if (authToken is null)
        {
            await Unauthorized("Include token", context);
            return;
        }

        var apps = db.OauthApps.Where(x => x.OwnerId == authToken.UserId);

        foreach (var app in apps)
        {
            // If not owner, hide secret
            if (authToken.UserId != app.OwnerId)
            {
                app.Secret = "";
            }
        }

        context.Response.StatusCode = 200;
        await context.Response.WriteAsJsonAsync(apps);
    }

    public static async Task<IResult> GetApp(HttpContext context, ValourDb db, TokenService tokenService, long app_id,
    [FromHeader] string authorization)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        if (authToken is null)
            return ValourResult.InvalidToken();

        var app = await db.OauthApps.FindAsync(app_id);
        if (app is null)
            return ValourResult.NotFound("App not found");

        // If not owner, do not return
        if (authToken.UserId != app.OwnerId)
            return ValourResult.InvalidToken();

        return Results.Json(app);
    }

    public static async Task<IResult> GetAppPublic(HttpContext context, ValourDb db, TokenService tokenService, long app_id,
    [FromHeader] string authorization)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        if (authToken is null)
            return ValourResult.InvalidToken();

        var app = await db.OauthApps.FindAsync(app_id);
        if (app is null)
            return ValourResult.NotFound("App not found");

        PublicOauthAppData publicData = new()
        {
            Id = app.Id,
            Name = app.Name,
            ImageUrl = app.ImageUrl,
            RedirectUrl = app.RedirectUrl,
        };

        return Results.Json(publicData);
    }

    public static async Task<IResult> CreateApp(HttpContext context, ValourDb db, TokenService tokenService, [FromBody] OauthApp app, [FromHeader] string authorization)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        if (authToken is null)
            return ValourResult.NoToken();

        if (app is null)
            return ValourResult.BadRequest("Include app in body");

        if (app.RedirectUrl is null)
            app.RedirectUrl = string.Empty;

        if (await db.OauthApps.CountAsync(x => x.OwnerId == authToken.UserId) > 9)
            return ValourResult.BadRequest("There is currently a 10 app limit!");

        // Ensure variables are correctly set
        app.OwnerId = authToken.UserId;
        app.Uses = 0;
        app.ImageUrl = "../_content/Valour.Client/media/logo/logo-512.png";

        // Make name conform to server rules
        var nameValid = PlanetService.ValidateName(app.Name);

        if (!nameValid.Success)
            return ValourResult.BadRequest(nameValid.Message);

        // Generate secret token

        string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        app.Secret = secret;

        app.Id = IdManager.Generate();

        db.OauthApps.Add(app.ToDatabase());
        await db.SaveChangesAsync();

        return ValourResult.Ok(app.Id.ToString());
    }
}