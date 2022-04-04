using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Valour.Shared.Items.Messages.Embeds;
using Valour.Shared.Items.Messages.Mentions;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Items.Messages;

public class MessageBase : ISharedItem
{
    /// <summary>
    /// The mentions contained within this message
    /// </summary>
    private List<Mention> _mentions;

    /// <summary>
    /// True if the mentions data has been parsed
    /// </summary>
    private bool mentionsParsed = false;

    /// <summary>
    /// The inner embed data
    /// </summary>
    private Embed _embed;

    /// <summary>
    /// True if the embed data has been parsed
    /// </summary>
    private bool embedParsed = false;

    /// <summary>
    /// The mentions for members within this message
    /// </summary>
    [NotMapped]
    public List<Mention> Mentions
    {
        get
        {
            if (!mentionsParsed)
            {
                if (!string.IsNullOrEmpty(MentionsData))
                {
                    _mentions = JsonSerializer.Deserialize<List<Mention>>(MentionsData);
                }
            }

            return _mentions;
        }
    }

    [NotMapped]
    public Embed Embed
    {
        get
        {
            if (!embedParsed)
            {
                if (!string.IsNullOrEmpty(EmbedData))
                {
                    _embed = JsonSerializer.Deserialize<Embed>(EmbedData);
                }

                embedParsed = true;
            }

            return _embed;
        }
    }

    public void SetMentions(IEnumerable<Mention> mentions)
    {
        _mentions = mentions.ToList();
        MentionsData = JsonSerializer.Serialize(mentions);
    }

    public void ClearMentions()
    {
        if (_mentions == null)
        {
            _mentions = new List<Mention>();
        }
        else
        {
            _mentions.Clear();
        }
    }

    /// <summary>
    /// The user's ID
    /// </summary>
    [JsonPropertyName("Author_Id")]
    public ulong Author_Id { get; set; }

    /// <summary>
    /// The member's ID
    /// </summary>
    [JsonPropertyName("Member_Id")]
    public ulong Member_Id { get; set; }

    /// <summary>
    /// String representation of message
    /// </summary>
    [JsonPropertyName("Content")]
    public string Content { get; set; }

    /// <summary>
    /// The time the message was sent (in UTC)
    /// </summary>
    [JsonPropertyName("TimeSent")]
    public DateTime TimeSent { get; set; }

    /// <summary>
    /// Id of the channel this message belonged to
    /// </summary>
    [JsonPropertyName("Channel_Id")]
    public ulong Channel_Id { get; set; }

    /// <summary>
    /// Index of the message
    /// </summary>
    [JsonPropertyName("MessageIndex")]
    public ulong MessageIndex { get; set; }

    /// <summary>
    /// Data for representing an embed
    /// </summary>
    [JsonPropertyName("EmbedData")]
    public string EmbedData { get; set; }

    /// <summary>
    /// Data for representing mentions in a message
    /// </summary>
    [JsonPropertyName("MentionsData")]
    public string MentionsData { get; set; }

    /// <summary>
    /// Used to identify a message returned from the server 
    /// </summary>
    [NotMapped]
    [JsonInclude]
    [JsonPropertyName("Fingerprint")]
    public string Fingerprint { get; set; }

    [NotMapped]
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => throw new System.NotImplementedException();

    /// <summary>
    /// Returns the hash for a message.
    /// </summary>
    public byte[] GetHash()
    {
        using (SHA256 sha = SHA256.Create())
        {
            string conc = $"{Author_Id}{Content}{TimeSent}{Channel_Id}{Message_Index}{Embed_Data}";

            byte[] buffer = Encoding.Unicode.GetBytes(conc);

            return sha.ComputeHash(buffer);
        }
    }

    /// <summary>
    /// Returns true if the message is a embed
    /// </summary>
    public bool IsEmbed()
    {
        if (EmbedData != null)
        {
            return true;
        }
        else
        {
            return false;
        }
    }
}

