using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Nodes;

namespace Valour.Server.Api.Dynamic;

public class UserCommunityNodeApi
{
    [ValourRoute(HttpVerbs.Get, "api/users/me/communitynodes")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetSavedNodesRouteAsync(
        UserCommunityNodeService userCommunityNodeService,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var nodes = await userCommunityNodeService.GetForUserAsync(userId);
        return Results.Json(nodes);
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/communitynodes")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> AddSavedNodeRouteAsync(
        [FromBody] AddCommunityNodeRequest request,
        UserCommunityNodeService userCommunityNodeService,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await userCommunityNodeService.AddAsync(userId, request?.Origin);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/users/me/communitynodes/{savedNodeId}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> DeleteSavedNodeRouteAsync(
        long savedNodeId,
        UserCommunityNodeService userCommunityNodeService,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await userCommunityNodeService.RemoveAsync(userId, savedNodeId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Ok(result.Message);
    }
}
