using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Valour.Server.Oauth;
using Valour.Server.Database;
using Valour.Server.Planets;
using System.Security.Cryptography;

namespace Valour.Server.API;

public class OauthAPI : BaseAPI 
{
    /// <summary>
    /// Adds the routes for this API section
    /// </summary>
    public static void AddRoutes(WebApplication app)
    {
        app.MapPost("/api/oauth/app", CreateApp);
        app.MapGet("api/oauth/app/{app_id}", GetApp);
    }

    public static async Task GetApp(HttpContext context, ValourDB db, ulong app_id)
    {
        var app = await db.OauthApps.FindAsync(app_id);

        if (app is null){
            await NotFound("App not found", context);
            return;
        }

        context.Response.StatusCode = 200;
        await context.Response.WriteAsJsonAsync(app);
    }

    public static async Task CreateApp(HttpContext context, ValourDB db, [FromBody] OauthApp app, [FromHeader] string authorization)
    {
        var authToken = await ServerAuthToken.TryAuthorize(authorization, db);

        if (authToken is null){
            await Unauthorized("Include token", context);
            return;
        }

        if (app is null){
            await BadRequest("Include app in body", context);
            return;
        }

        if (await db.OauthApps.CountAsync(x => x.Owner_Id == authToken.User_Id) > 9)
        {
            await BadRequest("There is currently a 10 app limit!", context);
            return;
        }

        // Ensure variables are correctly set
        app.Owner_Id = authToken.User_Id;
        app.Uses = 0;
        app.Image_Url = "media/logo/logo-512.png";
        
        // Make name conform to server rules
        var nameValid = ServerPlanet.ValidateName(app.Name);

        if (!nameValid.Success){
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