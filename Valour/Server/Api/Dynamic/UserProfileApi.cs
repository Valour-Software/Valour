using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class UserProfileApi
{
    [ValourRoute(HttpVerbs.Get, "api/userProfiles/{id}")]
    public static async Task<IResult> GetUserProfileAsync(long id, UserService userService)
    {
        var profile = await userService.GetUserProfileAsync(id);
        return profile is null ? ValourResult.NotFound<UserProfile>() : Results.Json(profile);
    }

    [ValourRoute(HttpVerbs.Put, "api/userProfiles/{id}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> UpdateSelfProfileAsync([FromBody] UserProfile profile, long id, UserService userService)
    {
        if (id != profile.Id)
            return ValourResult.BadRequest("Id mismatch.");
        
        var userId = await userService.GetCurrentUserIdAsync();
        if (profile.Id != userId)
            return ValourResult.Forbid("You can only change your own profile.");
        
        var result = await userService.UpdateUserProfileAsync(profile);
        if (!result.Success)
            return ValourResult.Problem(result.Message);
        
        return Results.Json(result.Data);
    }
}