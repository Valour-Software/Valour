using Valour.Server.Database.Items.Users;
using Valour.Shared.Items.Channels.Users;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database.Items.Channels.Users;

[Table("direct_chat_channels")]
public class DirectChatChannel : Channel, ISharedDirectChatChannel
{
    /// <summary>
    /// One of the users in the DM channel
    /// </summary>
    [ForeignKey("UserOneId")]
    public virtual User UserOne { get; set; }

    /// <summary>
    /// One of the users in the DM channel
    /// </summary>
    [ForeignKey("UserTwoId")]
    public virtual User UserTwo { get; set; }

    /// <summary>
    /// The id of one of the users in the DM channel
    /// </summary>
    [Column("user_one_id")]
    public long UserOneId { get; set; }

    /// <summary>
    /// The id of one of the users in the DM channel
    /// </summary>
    [Column("user_two_id")]
    public long UserTwoId { get; set; }

    [Column("message_count")]
    public long MessageCount { get; set; }
}
