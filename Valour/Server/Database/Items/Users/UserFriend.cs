using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Users;

namespace Valour.Server.Database.Items.Users;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

/// <summary>
/// Friend objects represent a one-way declaration of friendship. To be actual friends, there must be a
/// UserFriend object going both ways. Like, you know, a real friendship.
/// 
/// ... I'll be your friend!
/// </summary>
[Table("user_friends")]
public class UserFriend : Item, ISharedUserFriend
{
    /// <summary>
    /// The user who added the friend
    /// </summary>
    [ForeignKey("UserId")]
    public virtual User User { get; set; }

    /// <summary>
    /// The user being added as a friend
    /// (friendzoned)
    /// </summary>
    [ForeignKey("FriendId")]
    public virtual User Friend { get; set; }

    /// <summary>
    /// The id of the user who added the friend
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }

    /// <summary>
    /// The id of the user being added as a friend
    /// (friendzoned)
    /// </summary>
    [Column("friend_id")]
    public long FriendId { get; set; }

    [JsonIgnore]
    public override string BaseRoute =>
        $"api/{nameof(UserFriend)}/{UserId}/{FriendId}";

    [ValourRoute(HttpVerbs.Get, "/{userId}/{friendId}", $"api/{nameof(UserFriend)}"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Friends)]
    public static async Task<IResult> GetFriendRouteAsync(HttpContext ctx, long userId, long friendId)
    {
        var token = ctx.GetToken();
        var db = ctx.GetDb();

        /* TODO: In the future, allow users to enable other users seeing their friends */
        if (token.UserId != userId)
            return ValourResult.Forbid("You cannot currently view another user's friends.");

        var friend = await db.UserFriends.FirstOrDefaultAsync(x => x.UserId == userId &&
                                                                   x.FriendId == friendId);

        if (friend is null)
            return ValourResult.NotFound("Friend not found.");

        return Results.Json(friend);
    }

    [ValourRoute(HttpVerbs.Post, "/remove/{friendUsername}", $"api/{nameof(UserFriend)}"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Friends)]
    public static async Task<IResult> RemoveFriendRouteAsync(HttpContext ctx, [FromRoute] string friendUsername)
    {
        var token = ctx.GetToken();
        var db = ctx.GetDb();

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

    [ValourRoute(HttpVerbs.Post, "/add/{friendUsername}", $"api/{nameof(UserFriend)}"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Friends)]
    public static async Task<IResult> AddFriendRouteAsync(HttpContext ctx, [FromRoute] string friendUsername)
    {
        var token = ctx.GetToken();
        var db = ctx.GetDb();

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
}
