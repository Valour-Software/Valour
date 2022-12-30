using Microsoft.AspNetCore.Mvc;
using Valour.Database;
using Valour.Server.EndpointFilters;
using Valour.Server.EndpointFilters.Attributes;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class OauthAppAPI
{
    [ValourRoute(HttpVerbs.Put), TokenRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] OauthApp app, 
        HttpContext ctx,
        ValourDB db,
        ILogger<User> logger)
    {
        var token = ctx.GetToken();

        // Unlike most other entities, we are just copying over a few fields here and
        // ignoring the rest. There are so many things that *should not* be touched by
        // the API it's smarter to just only do what *should*

        if (app.OwnerId != token.UserId)
            return ValourResult.Forbid("You can only change your own applications.");

        var old = await FindAsync<OauthApp>(app.Id, db);

        old.RedirectUrl = app.RedirectUrl;

        try
        {
            db.OauthApps.Update(old);
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e.Message);
            return ValourResult.Problem(e.Message);
        }

        return Results.Json(old);
    }
}