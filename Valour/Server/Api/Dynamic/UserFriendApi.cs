namespace Valour.Server.Api.Dynamic;

public class UserFriendApi
{
	[ValourRoute(HttpVerbs.Get, "/{userId}/{friendId}", $"api/{nameof(UserFriend)}"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Friends)]
    public static async Task<IResult> GetFriendRouteAsync(
	    long userId, 
	    long friendId, 
	    HttpContext ctx, 
	    ValourDB db)
    {
        var token = ctx.GetToken();
        /* TODO: In the future, allow users to enable other users seeing their friends */
        if (token.UserId != userId)
            return ValourResult.Forbid("You cannot currently view another user's friends.");

        var friend = await db.UserFriends.FirstOrDefaultAsync(x => x.UserId == userId &&
                                                                   x.FriendId == friendId);

        if (friend is null)
            return ValourResult.NotFound("Friend not found.");

        return Results.Json(friend);
    }

    [ValourRoute(HttpVerbs.Post, "/remove/{friendUsername}", $"api/{nameof(UserFriend)}"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Friends)]
    public static async Task<IResult> RemoveFriendRouteAsync(
	    [FromRoute] string friendUsername, 
	    HttpContext ctx,
	    ValourDB db)
    {
        var token = ctx.GetToken();

        /* TODO: Eventually ensure user is not blocked */

        var friendUser = await db.Users.FirstOrDefaultAsync(x => x.Name.ToLower() == friendUsername.ToLower());
        if (friendUser is null)
            return ValourResult.NotFound($"User {friendUsername} was not found.");

        var friend = await db.UserFriends.FirstOrDefaultAsync(x => x.UserId == token.UserId &&
                                                                   x.FriendId == friendUser.Id);
        if (friend is null)
            return ValourResult.BadRequest("User is already not a friend.");

        db.UserFriends.Remove(friend);
        await db.SaveChangesAsync();

        return ValourResult.Ok("Friendship removed successfully.");
    }

    [ValourRoute(HttpVerbs.Post, "/add/{friendUsername}", $"api/{nameof(UserFriend)}"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Friends)]
    public static async Task<IResult> AddFriendRouteAsync(
	    [FromRoute] string friendUsername, 
	    HttpContext ctx, 
	    ValourDB db)
    {
        var token = ctx.GetToken();

        /* TODO: Eventually ensure user is not blocked */

        var friendUser = await db.Users.FirstOrDefaultAsync(x => x.Name.ToLower() == friendUsername.ToLower());
        if (friendUser is null)
            return ValourResult.NotFound($"User {friendUsername} was not found.");

        if (await db.UserFriends.AnyAsync(x => x.UserId == token.UserId &&
                                               x.FriendId == friendUser.Id))
            return ValourResult.BadRequest("Friend already added.");

        UserFriend newFriend = new()
        {
            Id = IdManager.Generate(),
            UserId = token.UserId,
            FriendId = friendUser.Id,
        };

        await db.UserFriends.AddAsync(newFriend);
        await db.SaveChangesAsync();

        return Results.Created(newFriend.GetUri(), newFriend);
    }

	[ValourRoute(HttpVerbs.Post, "/decline/{username}", $"api/{nameof(UserFriend)}"), TokenRequired]
	[UserPermissionsRequired(UserPermissionsEnum.Friends)]
	public static async Task<IResult> DeclineFriendRouteAsync(
		[FromRoute] string username, 
		HttpContext ctx, 
		ValourDB db)
	{
		var token = ctx.GetToken();

		var requestUser = await db.Users.FirstOrDefaultAsync(x => x.Name.ToLower() == username.ToLower());
		if (requestUser is null)
			return ValourResult.NotFound($"User {username} was not found.");

		var request = await db.UserFriends
			.FirstOrDefaultAsync(x => x.UserId == requestUser.Id &&
									  x.FriendId == token.UserId);

		if (request is null)
			return ValourResult.NotFound($"Friend request was not found.");

		db.UserFriends.Remove(request);
		await db.SaveChangesAsync();

		return ValourResult.Ok("Declined request");
	}

	[ValourRoute(HttpVerbs.Post, "/cancel/{username}", $"api/{nameof(UserFriend)}")]
	[UserPermissionsRequired(UserPermissionsEnum.Friends)]
	public static async Task<IResult> CancelFriendRouteAsync(
		[FromRoute] string username, 
		HttpContext ctx, 
		ValourDB db)
	{
		var token = ctx.GetToken();
		
		var targetUser = await db.Users.FirstOrDefaultAsync(x => x.Name.ToLower() == username.ToLower());
		if (targetUser is null)
			return ValourResult.NotFound($"User {username} was not found.");

		var request = await db.UserFriends
			.FirstOrDefaultAsync(x => x.UserId == token.UserId &&
									  x.FriendId == targetUser.Id);

		if (request is null)
			return ValourResult.NotFound($"Friend request was not found.");

		db.UserFriends.Remove(request);
		await db.SaveChangesAsync();

		return ValourResult.Ok("Cancelled request");
	}
}