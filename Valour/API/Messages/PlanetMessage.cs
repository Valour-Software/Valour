using System.Text.Json;
using Valour.Api.Planets;
using System.Text.Json.Serialization;
using static Valour.Api.Client.ValourClient;

namespace Valour.Api.Messages;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetMessage : Shared.Messages.PlanetMessage
{
    // Makes PlanetMessage meant to be sent to valour from the client
    public PlanetMessage(string text, ulong self_member_id, ulong channel_id, ulong planet_id)
    {
        Channel_Id = channel_id;
        Content = text;
        TimeSent = DateTime.UtcNow;
        Author_Id = Self.Id;
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
    public async Task<Member> GetAuthorAsync() =>
        await Member.FindAsync(Member_Id);

    /// <summary>
    /// Returns the planet the message was sent in
    /// </summary>
    public async Task<Planet> GetPlanetAsync() =>
        await Planet.FindAsync(Planet_Id);

    /// <summary>
    /// Returns the channel the message was sent in
    /// </summary>
    public async Task<Channel> GetChannelAsync() =>
        await Channel.FindAsync(Channel_Id);

    /// <summary>
    /// Sends a message to the channel this message was sent in
    /// </summary>
    public async Task ReplyAsync(string text) =>
        await SendMessage(new(text, (await GetSelfMember(Planet_Id)).Id, Channel_Id, Planet_Id));

    /// <summary>
    /// Sends a message with a embed to the channel this message was sent in
    /// </summary>
    public async Task ReplyAsync(string text = "", ClientEmbed embed = null)
    {
        PlanetMessage message = new(text, (await GetSelfMember(Planet_Id)).Id, Channel_Id, Planet_Id);

        if (embed is not null)
        {
            if (embed.Items.Count != 0) embed.Pages.Insert(0, embed.Items);

            JsonSerializerOptions options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
            message.Embed_Data = JsonSerializer.Serialize(embed, options);
        }

        await SendMessage(message);
    }
}

