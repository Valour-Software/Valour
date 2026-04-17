using Microsoft.AspNetCore.Mvc;
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
            return ValourResult.Problem(result.Message);

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

    [ValourRoute(HttpVerbs.Post, "api/oauth/authorize")]
    [UserRequired]
    public static async Task<IResult> AuthorizeAsync(
        HttpContext context,
        ValourDb db,
        [FromBody] AuthorizeModel model,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        if (model.UserId != userId)
            return ValourResult.InvalidToken();

        var client = await db.OauthApps.FindAsync(model.ClientId);
        if (client is null)
            return ValourResult.NotFound($"App with id {model.ClientId} not found");

        if (client.RedirectUrl != model.RedirectUri)
            return ValourResult.Problem("Client redirect url does not match given url");

        model.Code = Guid.NewGuid().ToString();

        var cachedCode = new CachedOAuthCode
        {
            Model = model,
            CreatedAt = DateTime.UtcNow
        };
        OauthCodeCache.TryAdd(model.Code, cachedCode);

        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");

        return ValourResult.Ok($"{model.RedirectUri}?code={model.Code}&state={model.State}&node={NodeConfig.Instance.Name}");
    }

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
        if (app is null || app.Secret != request.ClientSecret)
            return ValourResult.Forbid("Parameters are invalid.");

        // Remove the code from cache - codes are single-use per RFC 6749
        OauthCodeCache.TryRemove(request.Code, out _);

        switch (request.GrantType)
        {
            case "authorization_code":
            {
                AuthToken newToken = new AuthToken()
                {
                    Id = "val-" + Guid.NewGuid().ToString(),
                    TokenType = "oauth",
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

            default:
                return ValourResult.Problem("Available grant types: authorization_code");
        }
    }

    #endregion
}
