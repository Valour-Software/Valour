using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class OauthAppApi
{
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

        // Unlike most other entities, we are just copying over a few fields here and
        // ignoring the rest. There are so many things that *should not* be touched by
        // the API it's smarter to just only do what *should*

        var result = await oauthAppService.UpdateAsync(app);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }
}