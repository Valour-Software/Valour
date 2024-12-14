using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

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
public class UserFriend : ClientModel<UserFriend, long>, ISharedUserFriend
{
    public override string BaseRoute => ISharedUserFriend.BaseRoute;
    
    /// <summary>
    /// The id of the user who added the friend
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// The id of the user being added as a friend
    /// (friendzoned)
    /// </summary>
    public long FriendId { get; set; }

    public override UserFriend AddToCacheOrReturnExisting()
    {
        return Client.Cache.UserFriends.Put(Id, this);
    }

    public override UserFriend TakeAndRemoveFromCache()
    {
        return Client.Cache.UserFriends.TakeAndRemove(Id);
    }
}
