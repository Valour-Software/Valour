using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Database.Items.Users;
using Valour.Shared.Items;
using Valour.Shared.Items.Messages;

namespace Valour.Server.Database.Items.Messages;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PlanetMessage : PlanetItem, ISharedPlanetMessage
{
    [ForeignKey("AuthorId")]
    public User Author { get; set; }

    [ForeignKey("MemberId")]
    public PlanetMember User { get; set; }

    /// <summary>
    /// The author's user ID
    /// </summary>
    public ulong AuthorId { get; set; }

    /// <summary>
    /// The author's member ID
    /// </summary>
    public ulong MemberId { get; set; }

    /// <summary>
    /// String representation of message
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// The time the message was sent (in UTC)
    /// </summary>
    public DateTime TimeSent { get; set; }

    /// <summary>
    /// Id of the channel this message belonged to
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    /// Index of the message
    /// </summary>
    public ulong MessageIndex { get; set; }

    /// <summary>
    /// Data for representing an embed
    /// </summary>
    public string EmbedData { get; set; }

    /// <summary>
    /// Data for representing mentions in a message
    /// </summary>
    public string MentionsData { get; set; }

    /// <summary>
    /// Used to identify a message returned from the server 
    /// </summary>
    public string Fingerprint { get; set; }

    [JsonInclude]
    [JsonPropertyName("itemType")]
    public static string _ItemType => nameof(PlanetMessage);
}

