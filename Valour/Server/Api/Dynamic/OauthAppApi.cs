using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Valour.Config.Configs;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

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

public class OauthAppApi
{
    /// <summary>
    /// Cache for OAuth authorization codes with expiration tracking
    /// </summary>
    public static ConcurrentDictionary<string, CachedOAuthCode> OauthCodeCache = new();

    /// <summary>
    /// Every scope bit an app can be granted. Full control is deliberately not
    /// grantable: it would let an app manage sessions, credentials, and other apps.
    /// </summary>
    private static readonly long GrantableScopeMask = UserPermissions.Permissions
        .Where(x => x.Value != Permission.FULL_CONTROL)
        .Aggregate(0L, (mask, permission) => mask | permission.Value);

    /// <summary>
    /// Starts the background cleanup task for expired OAuth codes.
    /// Called from Program.cs during startup.
    /// </summary>
    public static void StartCodeCleanupTask()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(5));

                    var expiredCodes = OauthCodeCache
                        .Where(kvp => kvp.Value.IsExpired)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var code in expiredCodes)
                        OauthCodeCache.TryRemove(code, out _);

                    if (expiredCodes.Count > 0)
                        Console.WriteLine($"Cleaned up {expiredCodes.Count} expired OAuth codes");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error cleaning up OAuth codes: {ex.Message}");
                }
            }
        });
    }

    #region App CRUD

    [ValourRoute(HttpVerbs.Post, "api/oauthapps")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> CreateAsync(
        [FromBody] OauthApp app,
        OauthAppService oauthAppService,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        if (app is null)
            return ValourResult.BadRequest("Include app in body");

        var result = await oauthAppService.CreateAsync(app, userId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok(result.Data.Id.ToString());
    }

    [ValourRoute(HttpVerbs.Get, "api/oauthapps")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetAllAsync(
        OauthAppService oauthAppService,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var apps = await oauthAppService.GetAllByOwnerAsync(userId);
        return Results.Json(apps);
    }

    [ValourRoute(HttpVerbs.Get, "api/oauthapps/{id}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetAsync(
        long id,
        OauthAppService oauthAppService,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var app = await oauthAppService.GetAsync(id);

        if (app is null)
            return ValourResult.NotFound("App not found");

        if (app.OwnerId != userId)
            return ValourResult.Forbid("You can only view your own applications.");

        return Results.Json(app);
    }

    [ValourRoute(HttpVerbs.Get, "api/oauthapps/public/{id}")]
    [UserRequired]
    public static async Task<IResult> GetPublicAsync(
        long id,
        OauthAppService oauthAppService)
    {
        var app = await oauthAppService.GetAsync(id);

        if (app is null)
            return ValourResult.NotFound("App not found");

        var publicData = new PublicOauthAppData
        {
            Id = app.Id,
            Name = app.Name,
            ImageUrl = app.ImageUrl,
            RedirectUrl = app.RedirectUrl,
        };

        return Results.Json(publicData);
    }

    [ValourRoute(HttpVerbs.Put, "api/oauthapps/{id}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] OauthApp app,
        long id,
        OauthAppService oauthAppService,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        if (app.Id != id)
            return ValourResult.BadRequest("Route id does not match app id");

        var ownsApp = await oauthAppService.OwnsAppAsync(userId, app.Id);
        if (!ownsApp)
            return ValourResult.Forbid("You can only change your own applications.");

        var result = await oauthAppService.UpdateAsync(app);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/oauthapps/{id}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> DeleteRouteAsync(
        long id,
        OauthAppService oauthAppService,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        var ownsApp = await oauthAppService.OwnsAppAsync(userId, id);
        if (!ownsApp)
            return ValourResult.Forbid("You can only delete your own applications.");

        var result = await oauthAppService.DeleteAsync(id);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Ok();
    }

    #endregion

    #region OAuth Protocol (external-facing)

    // Granting scopes to an app is an account-level action, so it needs a
    // first-party (full control) session. Otherwise an app holding a narrow
    // token could authorize itself again with a wider scope.
    [ValourRoute(HttpVerbs.Post, "api/oauth/authorize")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> AuthorizeAsync(
        HttpContext context,
        ValourDb db,
        [FromBody] AuthorizeModel model,
        UserService userService)
    {
        if (model is null)
            return ValourResult.BadRequest("Include request in body.");

        var userId = await userService.GetCurrentUserIdAsync();
        if (model.UserId != userId)
            return ValourResult.InvalidToken();

        if (model.Scope < 0 || (model.Scope & ~GrantableScopeMask) != 0)
            return ValourResult.BadRequest("The requested scope contains permissions that cannot be granted.");

        var client = await db.OauthApps.FindAsync(model.ClientId);
        if (client is null)
            return ValourResult.NotFound($"App with id {model.ClientId} not found");

        // Apps saved before redirect URLs were validated may hold an unsafe one
        if (!OauthAppService.ValidateRedirectUrl(client.RedirectUrl).Success)
            return ValourResult.BadRequest("This app does not have a valid redirect URL. Its owner must update it before it can be authorized.");

        if (client.RedirectUrl != model.RedirectUri)
            return ValourResult.Problem("Client redirect url does not match given url");

        model.Code = Guid.NewGuid().ToString();

        var cachedCode = new CachedOAuthCode
        {
            Model = model,
            CreatedAt = DateTime.UtcNow
        };
        OauthCodeCache.TryAdd(model.Code, cachedCode);

        context.Response.Headers["Access-Control-Allow-Origin"] = "*";

        // Encode every value so a crafted state cannot add or override parameters
        var query = new Dictionary<string, string>
        {
            ["code"] = model.Code,
            ["state"] = model.State ?? string.Empty,
        };
        if (!string.IsNullOrEmpty(NodeConfig.Instance?.Name))
            query["node"] = NodeConfig.Instance.Name;

        return ValourResult.Json(QueryHelpers.AddQueryString(model.RedirectUri, query));
    }

    [RateLimit(RateLimitPolicies.Auth)]
    [ValourRoute(HttpVerbs.Post, "api/oauth/token")]
    public static async Task<IResult> TokenAsync(
        ValourDb db,
        [FromBody] OauthTokenExchangeRequest request)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        if (request.ClientId <= 0 ||
            string.IsNullOrWhiteSpace(request.ClientSecret) ||
            string.IsNullOrWhiteSpace(request.GrantType) ||
            string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.RedirectUri))
        {
            return ValourResult.BadRequest("Missing required OAuth token parameters.");
        }

        if (request.GrantType != "authorization_code")
            return ValourResult.Problem("Available grant types: authorization_code");

        OauthCodeCache.TryGetValue(request.Code, out var cached);
        if (cached is null)
            return ValourResult.Forbid("Invalid or expired authorization code.");

        if (cached.IsExpired)
        {
            OauthCodeCache.TryRemove(request.Code, out _);
            return ValourResult.Forbid("Authorization code has expired. Please re-authorize.");
        }

        var model = cached.Model;
        if (model.ClientId != request.ClientId ||
            model.RedirectUri != request.RedirectUri ||
            model.State != request.State)
            return ValourResult.Forbid("Parameters are invalid.");

        var app = await db.OauthApps.FindAsync(request.ClientId);
        // Constant-time: the attacker fully controls one side of this comparison
        // and the other is a stable server secret, which is the classic shape
        // for a timing side channel.
        if (app is null || !SecretComparer.Equals(app.Secret, request.ClientSecret))
            return ValourResult.Forbid("Parameters are invalid.");

        // Codes are single-use per RFC 6749. Only the request that actually
        // removes the code may use it, so concurrent exchanges of one code
        // cannot both mint a token. Failed checks above leave the code in
        // place: burning it would let anyone who saw the code, but lacks the
        // client secret, cancel the legitimate exchange.
        if (!OauthCodeCache.TryRemove(request.Code, out _))
            return ValourResult.Forbid("Invalid or expired authorization code.");

        AuthToken newToken = new AuthToken()
        {
            Id = "val-" + Guid.NewGuid().ToString(),
            AppId = request.ClientId.ToString(),
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

    #endregion
}
