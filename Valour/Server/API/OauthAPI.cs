using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using Valour.Server.Database;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Planets;

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

        app.MapGet("api/user/{userId}/apps", GetApps);

        app.MapPost("api/oauth/authorize", Authorize);
    }

    public static List<Shared.Items.Authorization.AuthorizeModel> OauthReqCache = new();


    public static async Task<object> Authorize(
        ValourDB db, HttpContext context,
        [FromBody] Shared.Items.Authorization.AuthorizeModel model,
        [FromHeader] string authorization)
    {
        var authToken = await AuthToken.TryAuthorize(authorization, db);

        if (authToken is null)
        {
            await TokenInvalid(context);
            return null;
        }

        if (authToken.UserId != model.userId)
        {
            await Unauthorized("Token is invalid for this model", context);
            return null;
        }

        model.code = Guid.NewGuid().ToString();
        OauthReqCache.Add(model);

        //context.Response.Headers.Add("Access-Control-Allow-Origin", "*");

        return Results.Redirect(model.redirect_uri);// + $"?code={model.code}&state={model.state}");
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

    public static async Task GetApp(HttpContext context, ValourDB db, ulong app_id,
    [FromHeader] string authorization)
    {
        var authToken = await AuthToken.TryAuthorize(authorization, db);

        if (authToken is null)
        {
            await TokenInvalid(context);
            return;
        }

        var app = await db.OauthApps.FindAsync(app_id);

        if (app is null)
        {
            await NotFound("App not found", context);
            return;
        }

        // If not owner, hide secret
        if (authToken.UserId != app.OwnerId)
        {
            app.Secret = "";
        }

        context.Response.StatusCode = 200;
        await context.Response.WriteAsJsonAsync(app);
    }

    public static async Task CreateApp(HttpContext context, ValourDB db, [FromBody] OauthApp app, [FromHeader] string authorization)
    {
        var authToken = await AuthToken.TryAuthorize(authorization, db);

        if (authToken is null)
        {
            await Unauthorized("Include token", context);
            return;
        }

        if (app is null)
        {
            await BadRequest("Include app in body", context);
            return;
        }

        if (await db.OauthApps.CountAsync(x => x.OwnerId == authToken.UserId) > 9)
        {
            await BadRequest("There is currently a 10 app limit!", context);
            return;
        }

        // Ensure variables are correctly set
        app.OwnerId = authToken.UserId;
        app.Uses = 0;
        app.ImageUrl = "media/logo/logo-512.png";

        // Make name conform to server rules
        var nameValid = Planet.ValidateName(app.Name);

        if (!nameValid.Success)
        {
            await BadRequest(nameValid.Message, context);
            return;
        }

        // Generate secret token

        string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        app.Secret = secret;

        app.Id = IdManager.Generate();

        await db.OauthApps.AddAsync(app);
        await db.SaveChangesAsync();

        context.Response.StatusCode = 201;
        await context.Response.WriteAsJsonAsync(app.Id);
    }
}