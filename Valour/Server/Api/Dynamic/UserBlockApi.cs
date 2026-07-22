using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class UserBlockApi
{
    [ValourRoute(HttpVerbs.Post, "api/userblocks/{targetUserId}/{blockType}")]
    [UserRequired]
    public static async Task<IResult> BlockUserRouteAsync(
        long targetUserId,
        BlockType blockType,
        UserBlockService userBlockService,
        UserService userService)
    {
        if (!Enum.IsDefined(blockType))
            return ValourResult.BadRequest("Invalid block type.");

        var userId = await userService.GetCurrentUserIdAsync();

        var result = await userBlockService.BlockUserAsync(userId, targetUserId, blockType);
        if (!result.Success)
        {
            if (result.Message is "You cannot block yourself." or "User not found." or "User is already blocked.")
                return ValourResult.BadRequest(result.Message);

            return ValourResult.Problem(result.Message);
        }

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/userblocks/{targetUserId}")]
    [UserRequired]
    public static async Task<IResult> UnblockUserRouteAsync(
        long targetUserId,
        UserBlockService userBlockService,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        var result = await userBlockService.UnblockUserAsync(userId, targetUserId);
        if (!result.Success)
        {
            if (result.Message == "Block not found.")
                return ValourResult.BadRequest(result.Message);

            return ValourResult.Problem(result.Message);
        }

        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Get, "api/userblocks")]
    [ValourRoute(HttpVerbs.Get, "api/users/me/blocks")]
    [UserRequired]
    public static async Task<IResult> GetBlocksRouteAsync(
        UserBlockService userBlockService,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var blocks = await userBlockService.GetBlocksAsync(userId);
        return Results.Json(blocks);
    }
}
