using System.Text.Json;
using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Items.Planets;
using Valour.Api.Items.Planets.Members;
using Valour.Api.Items.Planets.Channels;

namespace Valour.Api.Items.Messages;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetMessage : Shared.Items.Messages.PlanetMessage
{
    // Makes PlanetMessage meant to be sent to valour from the client
    public PlanetMessage(string text, ulong self_member_id, ulong channel_id, ulong planet_id)
    {
        Channel_Id = channel_id;
        Content = text;
        TimeSent = DateTime.UtcNow;
        Author_Id = ValourClient.Self.Id;
        Planet_Id = planet_id;
        Member_Id = self_member_id;
        Fingerprint = Guid.NewGuid().ToString();
    }

    public PlanetMessage()
    {
    }

    /// <summary>
    /// Returns the author of the message
    /// </summary>
    public async Task<PlanetMember> GetAuthorAsync() =>
        await PlanetMember.FindAsync(Member_Id);

    /// <summary>
    /// Returns the planet the message was sent in
    /// </summary>
    public async Task<Planet> GetPlanetAsync() =>
        await Planet.FindAsync(Planet_Id);

    /// <summary>
    /// Returns the channel the message was sent in
    /// </summary>
    public async Task<ChatChannel> GetChannelAsync() =>
        await ChatChannel.FindAsync(Channel_Id);

    /// <summary>
    /// Sends a message to the channel this message was sent in
    /// </summary>
    public async Task ReplyAsync(string text) =>
        await ValourClient.SendMessage(new(text, (await ValourClient.GetSelfMember(Planet_Id)).Id, Channel_Id, Planet_Id));

    /// <summary>
    /// Sends a message with a embed to the channel this message was sent in
    /// </summary>
    public async Task ReplyAsync(string text = "", ClientEmbed embed = null)
    {
        PlanetMessage message = new(text, (await ValourClient.GetSelfMember(Planet_Id)).Id, Channel_Id, Planet_Id);

        if (embed is not null)
        {
            if (embed.Items.Count != 0) embed.Pages.Insert(0, embed.Items);

            JsonSerializerOptions options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
            message.Embed_Data = JsonSerializer.Serialize(embed, options);
        }

        await ValourClient.SendMessage(message);
    }
}

