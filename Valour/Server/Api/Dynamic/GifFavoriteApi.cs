using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using GifFavorite = Valour.Server.Models.GifFavorite;

namespace Valour.Server.Api.Dynamic;

public class GifFavoriteApi
{
    [ValourRoute(HttpVerbs.Post, "api/giffavorites")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> CreateAsync(
        [FromBody] GifFavorite favorite,
        GifFavoriteService gifFavoriteService,
        UserService userService)
    {
        favorite.UserId = await userService.GetCurrentUserIdAsync();
        var result = await gifFavoriteService.CreateAsync(favorite);
        return result.Success
            ? Results.Created($"api/giffavorites/{result.Data.Id}", result.Data)
            : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Delete, "api/giffavorites/{id}")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> DeleteAsync(
        long id,
        GifFavoriteService gifFavoriteService,
        UserService userService)
    {
        var favorite = await gifFavoriteService.GetAsync(id);
        if (favorite is null)
            return ValourResult.NotFound("GIF favorite not found.");

        if (favorite.UserId != await userService.GetCurrentUserIdAsync())
            return ValourResult.Forbid("You do not own this resource.");

        var result = await gifFavoriteService.DeleteAsync(favorite);
        return result.Success ? ValourResult.Ok("Deleted") : ValourResult.Problem(result.Message);
    }
}
