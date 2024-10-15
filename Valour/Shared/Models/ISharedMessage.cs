

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2024 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Models;

public interface ISharedMessage : ISharedModel
{
    /// <summary>
    /// The planet this message belongs to (if any)
    /// </summary>
    long? PlanetId { get; set; }
    
    /// <summary>
    /// The message (if any) this is a reply to
    /// </summary>
    long? ReplyToId { get; set; }

    /// <summary>
    /// The author's user ID
    /// </summary>
    long AuthorUserId { get; set; }
    
    /// <summary>
    /// The author's member ID (if this is a planet message)
    /// </summary>
    long? AuthorMemberId { get; set; }

    /// <summary>
    /// String representation of message
    /// </summary>
    string Content { get; set; }

    /// <summary>
    /// The time the message was sent (in UTC)
    /// </summary>
    DateTime TimeSent { get; set; }

    /// <summary>
    /// Id of the channel this message belonged to
    /// </summary>
    long ChannelId { get; set; }

    /// <summary>
    /// Data for representing an embed
    /// </summary>
    string EmbedData { get; set; }

    /// <summary>
    /// Data for representing mentions in a message
    /// </summary>
    string MentionsData { get; set; }

    /// <summary>
    /// Data for representing attachments in a message
    /// </summary>
    string AttachmentsData { get; set; }

    /// <summary>
    /// The time when the message was edited, or null if it was not
    /// </summary>
    public DateTime? EditedTime { get; set; }
}
