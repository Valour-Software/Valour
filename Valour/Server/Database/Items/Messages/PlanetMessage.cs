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

[Table("planet_messages")]
public class PlanetMessage : Item, IPlanetItem, ISharedPlanetMessage
{
    #region IPlanetItem Implementation

    [JsonIgnore]
    [ForeignKey("PlanetId")]
    public Planet Planet { get; set; }

    [Column("planet_id")]
    public long PlanetId { get; set; }

    public ValueTask<Planet> GetPlanetAsync(ValourDB db) =>
        IPlanetItem.GetPlanetAsync(this, db);

    [JsonIgnore]
    public override string BaseRoute =>
        $"api/planet/{{planetId}}/{nameof(PlanetMessage)}";

    #endregion

    [ForeignKey("AuthorUserId")]
    [JsonIgnore]
    public User Author { get; set; }

    [ForeignKey("AuthorMemberId")]
    [JsonIgnore]
    public PlanetMember User { get; set; }

    [ForeignKey("ReplyToId")]
    [JsonIgnore]
    public PlanetMessage ReplyToMessage { get; set; }

    /// <summary>
    /// The message (if any) this is a reply to
    /// </summary>
    [Column("reply_to_id")]
    public long? ReplyToId { get; set; }

    /// <summary>
    /// The author's user ID
    /// </summary>
    [Column("author_user_id")]
    public long AuthorUserId { get; set; }

    /// <summary>
    /// The author's member ID
    /// </summary>
    [Column("author_member_id")]
    public long AuthorMemberId { get; set; }

    /// <summary>
    /// String representation of message
    /// </summary>
    [Column("content")]
    public string Content { get; set; }

    /// <summary>
    /// The time the message was sent (in UTC)
    /// </summary>
    [Column("time_sent")]
    public DateTime TimeSent { get; set; }

    /// <summary>
    /// Id of the channel this message belonged to
    /// </summary>
    [Column("channel_id")]
    public long ChannelId { get; set; }

    /// <summary>
    /// Index of the message
    /// </summary>
    [Column("message_index")]
    public long MessageIndex { get; set; }

    /// <summary>
    /// Data for representing an embed
    /// </summary>
    [Column("embed_data")]
    public string EmbedData { get; set; }

    /// <summary>
    /// Data for representing mentions in a message
    /// </summary>
    [Column("mentions_data")]
    public string MentionsData { get; set; }

    /// <summary>
    /// Data for representing attachments in a message
    /// </summary>
    [Column("attachments_data")]
    public string AttachmentsData { get; set; }

    /// <summary>
    /// True if the message was edited
    /// </summary>
    [Column("edited")]
    public bool Edited { get; set; }

    /// <summary>
    /// Used to identify a message returned from the server 
    /// </summary>
    [NotMapped]
    public string Fingerprint { get; set; }
}

