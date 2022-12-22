using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using Valour.Shared.Items.Authorization;
using Valour.Server.Database;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Planets;
using Valour.Shared;
using System.Collections.Concurrent;
using Valour.Shared.Authorization;

namespace Valour.Server.API;

public class OauthAPI : BaseAPI
{
    /// <summary>
    /// Adds the routes for this API section
    /// </summary>
    public static void AddRoutes(WebApplication app)
    {
        app.MapPost("api/oauth/app", CreateApp);
        app.MapGet("api/oauth/app/{app_id}", GetApp);
        app.MapDelete("api/oauth/app/{app_id}", DeleteApp);
        app.MapGet("api/oauth/app/public/{app_id}", GetAppPublic);

        app.MapGet("api/user/{userId}/apps", GetApps);

        app.MapPost("api/oauth/authorize", Authorize);
        app.MapGet("api/oauth/token", Token);
    }

    // TODO: Clean this cache based on age of entry
    public static ConcurrentDictionary<string, AuthorizeModel> OauthReqCache = new();

    public static async Task<object> Authorize(
        ValourDB db, HttpContext context,
        [FromBody] AuthorizeModel model,
        [FromHeader] string authorization)
    {
        var authToken = await AuthToken.TryAuthorize(authorization, db);
        if (authToken is null || model.UserId != authToken.UserId)
            return ValourResult.InvalidToken();

        var client = await db.OauthApps.FindAsync(model.ClientId);
        if (client is null)
            return ValourResult.NotFound($"App with id {model.ClientId} not found");

        if (client.RedirectUrl != model.RedirectUri)
            return ValourResult.Problem("Client redirect url does not match given url");

        model.Code = Guid.NewGuid().ToString();
        OauthReqCache.TryAdd(model.Code, model);

        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");

        // Slightly different than normal oauth because of Blazor: We return the link for the app to redirect itself to
        return ValourResult.Ok($"{model.RedirectUri}?code={model.Code}&state={model.State}");
    }

    public static async Task<IResult> Token(
        ValourDB db,
        long client_id,
        string client_secret,
        string grant_type,
        string code,
        string redirect_uri,
        string state
    )
    {
        OauthReqCache.TryGetValue(code, out var model);
        if (model is null ||
            model.ClientId != client_id ||
            model.RedirectUri != redirect_uri ||
            model.State !=  state)
            return ValourResult.Forbid("Parameters are invalid.");

        var app = await db.OauthApps.FindAsync(client_id);
        if (app.Secret != client_secret)
            return ValourResult.Forbid("Parameters are invalid.");

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

                    await db.AuthTokens.AddAsync(newToken);
                    await db.SaveChangesAsync();

                    return Results.Json(newToken);
                }

            default:
                return ValourResult.Problem("Available grant types: authorization_code");
        }

    }

    public static async Task DeleteApp(HttpContext context, ValourDB db, ulong app_id, [FromHeader] string authorization)
    {
        var authToken = await AuthToken.TryAuthorize(authorization, db);

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

    public static async Task GetApps(HttpContext context, ValourDB db, [FromHeader] string authorization)
    {
        var authToken = await AuthToken.TryAuthorize(authorization, db);

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

    public static async Task<IResult> GetApp(HttpContext context, ValourDB db, long app_id,
    [FromHeader] string authorization)
    {
        var authToken = await AuthToken.TryAuthorize(authorization, db);
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

    public static async Task<IResult> GetAppPublic(HttpContext context, ValourDB db, long app_id,
    [FromHeader] string authorization)
    {
        var authToken = await AuthToken.TryAuthorize(authorization, db);
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

    public static async Task<IResult> CreateApp(HttpContext context, ValourDB db, [FromBody] OauthApp app, [FromHeader] string authorization)
    {
        var authToken = await AuthToken.TryAuthorize(authorization, db);
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
        var nameValid = Planet.ValidateName(app.Name);

        if (!nameValid.Success)
            return ValourResult.BadRequest(nameValid.Message);

        // Generate secret token

        string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        app.Secret = secret;

        app.Id = IdManager.Generate();

        db.OauthApps.Add(app);
        await db.SaveChangesAsync();

        return ValourResult.Ok(app.Id.ToString());
    }
}