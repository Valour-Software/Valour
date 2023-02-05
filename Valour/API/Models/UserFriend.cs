using Valour.Api.Models;
using Valour.Shared.Models;

namespace Valour.Api.Models;

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
public class UserFriend : Item, ISharedUserFriend
{
    #region IPlanetItem implementation

    public override string BaseRoute =>
            $"api/userfriends";

    #endregion
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
