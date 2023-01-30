using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class TenorFavoriteApi
{
    [ValourRoute(HttpVerbs.Post, "api/tenorfavorites")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> PostAsync(
        [FromBody] TenorFavorite favorite, 
        TenorFavoriteService tenorFavoriteService,
        UserService userService)
    {
        var user = await userService.GetCurrentUserAsync();

        favorite.UserId = user.Id;

        var result = await tenorFavoriteService.CreateAsync(favorite);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created($"api/tenorfavorites/{result.Data.Id}", result.Data);
    }
    
    [ValourRoute(HttpVerbs.Delete, "api/tenorfavorites/{id}")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> PostAsync(
        long id, 
        TenorFavoriteService tenorFavoriteService,
        UserService userService)
    {
        var user = await userService.GetCurrentUserAsync();

        var favorite = await tenorFavoriteService.GetAsync(id);

        if (favorite is null)
            return ValourResult.NotFound("Tenor favorite not found.");

        if (favorite.UserId != user.Id)
            return ValourResult.Forbid("You do not own this resource.");

        var result = await tenorFavoriteService.DeleteAsync(favorite);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return ValourResult.Ok("Deleted");
    }
}