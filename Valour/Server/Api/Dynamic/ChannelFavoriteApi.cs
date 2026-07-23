using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using ChannelFavorite = Valour.Server.Models.ChannelFavorite;

namespace Valour.Server.Api.Dynamic;

public class ChannelFavoriteApi
{
    [ValourRoute(HttpVerbs.Get, "api/users/me/channelfavorites")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetSelfAsync(
        ChannelFavoriteService channelFavoriteService,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        return Results.Json(await channelFavoriteService.GetForUserAsync(userId));
    }

    [ValourRoute(HttpVerbs.Post, "api/channelfavorites")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> CreateAsync(
        [FromBody] ChannelFavorite favorite,
        ChannelFavoriteService channelFavoriteService,
        UserService userService)
    {
        if (favorite is null)
            return ValourResult.BadRequest("Include favorite in body.");

        var userId = await userService.GetCurrentUserIdAsync();
        var result = await channelFavoriteService.CreateAsync(userId, favorite.ChannelId, favorite.PlanetId);
        return result.Success
            ? Results.Created($"api/channelfavorites/{result.Data.Id}", result.Data)
            : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Delete, "api/channelfavorites/by-channel/{channelId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> DeleteAsync(
        long channelId,
        ChannelFavoriteService channelFavoriteService,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await channelFavoriteService.DeleteAsync(userId, channelId);
        return result.Success ? ValourResult.Ok("Deleted") : ValourResult.Problem(result.Message);
    }

    [ValourRoute(HttpVerbs.Post, "api/channelfavorites/order")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> ReorderAsync(
        [FromBody] ReorderChannelFavoritesRequest request,
        ChannelFavoriteService channelFavoriteService,
        UserService userService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        var userId = await userService.GetCurrentUserIdAsync();
        var result = await channelFavoriteService.ReorderAsync(userId, request.PlanetId, request.ChannelIds);
        return result.Success ? ValourResult.Ok("Reordered") : ValourResult.BadRequest(result.Message);
    }
}
