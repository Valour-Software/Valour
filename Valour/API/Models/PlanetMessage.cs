using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Models.Messages.Embeds.Items;
using Valour.Api.Models.Messages.Embeds;
using Valour.Shared.Models;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Api.Models.Messages.Embeds;
using Valour.Api.Nodes;

namespace Valour.Api.Models;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetMessage : Message, IPlanetModel, ISharedPlanetMessage
{
    #region IPlanetModel implementation

    public long PlanetId { get; set; }

    public ValueTask<Planet> GetPlanetAsync(bool refresh = false) =>
        IPlanetModel.GetPlanetAsync(this, refresh);

    public override string BaseRoute =>
            $"api/chatchannels/{ChannelId}/messages/";

    #endregion

    public PlanetMessage ReplyTo { get; set; }
    
    public override PlanetMessage GetReply()
    {
        return ReplyTo as PlanetMessage; 
    }
    
    /// <summary>
    /// The member's ID
    /// </summary>
    public long AuthorMemberId { get; set; }

    public PlanetMessage()
    {
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


    public static async ValueTask<PlanetMessage> FindAsync(long id, long channelId, long planetId, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ValourCache.Get<PlanetMessage>(id);
            if (cached is not null)
                return cached;
        }

        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var response = await node.GetJsonAsync<PlanetMessage>($"api/chatchannels/{channelId}/message/{id}");
        var item = response.Data;

        if (item is not null)
        {
            await ValourCache.Put(id, item);
        }

        return item;
    }

    public override async Task<TaskResult> PostMessageAsync()
    {
        var node = await NodeManager.GetNodeForPlanetAsync(PlanetId);
        return await node.PostAsync($"api/chatchannels/{ChannelId}/messages", this);
    }

    /// <summary> 
    /// Returns the author member of the message 
    /// </summary> 
    public ValueTask<PlanetMember> GetAuthorMemberAsync() =>
        PlanetMember.FindAsync(AuthorMemberId, PlanetId);

    /// <summary>
    /// Returns the channel the message was sent in
    /// </summary>
    public ValueTask<PlanetChatChannel> GetChannelAsync() =>
        PlanetChatChannel.FindAsync(ChannelId, PlanetId);

    /// <summary>
    /// Attempts to delete this message
    /// </summary>
    public override Task<TaskResult> DeleteAsync() =>
        Node.DeleteAsync($"api/chatchannels/{ChannelId}/messages/{Id}");

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

    public override async ValueTask<string> GetAuthorNameAsync()
        => await (await GetAuthorMemberAsync()).GetNameAsync();

    public override async ValueTask<string> GetAuthorTagAsync()
    => (await (await GetAuthorMemberAsync()).GetPrimaryRoleAsync()).Name;

    public override async ValueTask<string> GetAuthorColorAsync()
        => await (await GetAuthorMemberAsync()).GetRoleColorAsync();

    public override async ValueTask<string> GetAuthorImageUrlAsync()
        => await (await GetAuthorMemberAsync()).GetPfpUrlAsync();

    public override async ValueTask<Message> GetReplyMessageAsync()
    {
        if (ReplyToId is null)
            return null;

        return await FindAsync(ReplyToId.Value, ChannelId, PlanetId);
    }

    public override async Task<bool> CheckIfMentioned()
    {
        var selfMember = await PlanetMember.FindAsyncByUser(ValourClient.Self.Id, PlanetId);

        if (MentionsData is null)
            return false;

        return MentionsData.Contains(selfMember.Id.ToString());
    }

}

