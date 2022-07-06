using System.Text.Json;
using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Items.Users;
using Valour.Api.Items.Planets;
using Valour.Api.Items.Planets.Members;
using Valour.Api.Items.Planets.Channels;
using Valour.Shared.Items.Messages;
using Valour.Shared.Items.Messages.Embeds;
using Valour.Shared;
using Valour.Shared.Items;
using Valour.Shared.Items.Messages.Mentions;

namespace Valour.Api.Items.Messages;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetMessage : PlanetItem, ISharedPlanetMessage
{
    /// <summary>
    /// The user's ID
    /// </summary>
    public long AuthorUserId { get; set; }

    /// <summary>
    /// The member's ID
    /// </summary>
    public long AuthorMemberId { get; set; }

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
    public long ChannelId { get; set; }

    /// <summary>
    /// Index of the message
    /// </summary>
    public long MessageIndex { get; set; }

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

    // Makes PlanetMessage meant to be sent to valour from the client
    public PlanetMessage(string text, long self_memberId, long channelId, long planetId)
    {
        ChannelId = channelId;
        Content = text;
        TimeSent = DateTime.UtcNow;
        AuthorUserId = ValourClient.Self.Id;
        PlanetId = planetId;
        AuthorMemberId = self_memberId;
        Fingerprint = Guid.NewGuid().ToString();
    }

    public PlanetMessage()
    {
    }

    /// <summary> 
    /// Returns the author member of the message 
    /// </summary> 
    public async Task<PlanetMember> GetAuthorMemberAsync() =>
        await PlanetMember.FindAsync(AuthorMemberId, PlanetId);

    /// <summary> 
    /// Returns the author user of the message 
    /// </summary> 
    public async Task<User> GetAuthorUserAsync() =>
        await (await this.GetAuthorMemberAsync()).GetUserAsync();

    /// <summary>
    /// Returns the channel the message was sent in
    /// </summary>
    public async Task<PlanetChatChannel> GetChannelAsync() =>
        await PlanetChatChannel.FindAsync(ChannelId, PlanetId);

    /// <summary>
    /// Attempts to delete this message
    /// </summary>
    public async Task<TaskResult> DeleteAsync() =>
        await ValourClient.DeleteAsync($"api/channel/{ChannelId}/messages/{Id}");

    /// <summary>
    /// Sends a message to the channel this message was sent in
    /// </summary>
    public async Task ReplyAsync(string text) =>
        await ValourClient.SendMessage(new(text, (await ValourClient.GetSelfMember(PlanetId)).Id, ChannelId, PlanetId));

    /// <summary>
    /// Sends a message with a embed to the channel this message was sent in
    /// </summary>
    public async Task ReplyAsync(string text = "", Embed embed = null)
    {
        PlanetMessage message = new(text, (await ValourClient.GetSelfMember(PlanetId)).Id, ChannelId, PlanetId);

        if (embed is not null)
        {
            JsonSerializerOptions options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
            message.EmbedData = JsonSerializer.Serialize(embed, options);
        }

        await ValourClient.SendMessage(message);
    }
}

