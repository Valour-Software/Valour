namespace Valour.Shared.Models;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2024 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

/// <summary>
/// Friend objects represent a one-way declaration of friendship. To be actual friends, there must be a
/// UserFriend object going both ways. Like, you know, a real friendship.
/// 
/// ... I'll be your friend!
/// </summary>
public interface ISharedUserFriend : ISharedModel<long>
{
    const string BaseRoute = "api/userfriends";
    
    /// <summary>
    /// The user who added the friend
    /// </summary>
    long UserId { get; set; }

    /// <summary>
    /// The user being added as a friend
    /// (friendzoned)
    /// </summary>
    long FriendId { get; set; }
}
