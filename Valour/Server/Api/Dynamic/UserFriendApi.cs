using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class UserFriendApi
{
	[ValourRoute(HttpVerbs.Get, "api/userfriends/{userId}/{friendId}")]
    [UserRequired(UserPermissionsEnum.Friends)]
    public static async Task<IResult> GetFriendRouteAsync(
	    long userId, 
	    long friendId, 
	    UserFriendService userFriendService,
        UserService userService)
    {
        var requesteruserid = await userService.GetCurrentUserId();

        /* TODO: In the future, allow users to enable other users seeing their friends */
        if (requesteruserid != userId)
            return ValourResult.Forbid("You cannot currently view another user's friends.");

        var friend = await userFriendService.GetAsync(userId, friendId);

        if (friend is null)
            return ValourResult.NotFound("Friend not found.");

        return Results.Json(friend);
    }

    [ValourRoute(HttpVerbs.Post, "api/userfriends/remove/{friendUsername}")]
    [UserRequired(UserPermissionsEnum.Friends)]
    public static async Task<IResult> RemoveFriendRouteAsync(
	    [FromRoute] string friendUsername, 
        UserFriendService userFriendService,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserId();

        /* TODO: Eventually ensure user is not blocked */

        var result = await userFriendService.RemoveFriendAsync(friendUsername, userId);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return ValourResult.Ok("Friendship removed successfully.");
    }

    [ValourRoute(HttpVerbs.Post, "api/userfriends/add/{friendUsername}")]
    [UserRequired(UserPermissionsEnum.Friends)]
    public static async Task<IResult> AddFriendRouteAsync(
	    [FromRoute] string friendUsername,
        UserFriendService userFriendService,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserId();

        /* TODO: Eventually ensure user is not blocked */

        var result = await userFriendService.AddFriendAsync(friendUsername, userId);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created($"api/userfriends/{result.Data.UserId}/{result.Data.FriendId}", result.Data);
    }

	[ValourRoute(HttpVerbs.Post, "api/userfriends/decline/{username}")]
	[UserRequired(UserPermissionsEnum.Friends)]
	public static async Task<IResult> DeclineFriendRouteAsync(
		[FromRoute] string username,
        UserFriendService userFriendService,
        UserService userService)
	{
        var userId = await userService.GetCurrentUserId();

        var result = await userFriendService.DeclineRequestAsync(username, userId);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return ValourResult.Ok("Declined request");
	}

	[ValourRoute(HttpVerbs.Post, "api/userfriends/cancel/{username}")]
	[UserRequired(UserPermissionsEnum.Friends)]
	public static async Task<IResult> CancelFriendRouteAsync(
		[FromRoute] string username,
        UserFriendService userFriendService,
        UserService userService)
	{
        var userId = await userService.GetCurrentUserId(); ;

        var result = await userFriendService.CancelRequestAsync(username, userId);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return ValourResult.Ok("Cancelled request");
	}
}