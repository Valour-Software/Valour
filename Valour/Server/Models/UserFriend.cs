using Valour.Shared.Models;

namespace Valour.Server.Models;

/// <summary>
/// Friend objects represent a one-way declaration of friendship. To be actual friends, there must be a
/// UserFriend object going both ways. Like, you know, a real friendship.
/// 
/// ... I'll be your friend!
/// </summary>
public class UserFriend : Item, ISharedUserFriend
{
    /// <summary>
    /// The id of the user who added the friend
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// The id of the user being added as a friend
    /// (friendzoned)
    /// </summary>
    public long FriendId { get; set; }
}