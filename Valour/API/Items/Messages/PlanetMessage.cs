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
 
namespace Valour.Api.Items.Messages; 
 
/*  Valour - A free and secure chat client 
*  Copyright (C) 2021 Vooper Media LLC 
*  This program is subject to the GNU Affero General Public license 
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/> 
*/ 
 
public class PlanetMessage : PlanetMessageBase 
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
    /// Returns the author member of the message 
    /// </summary> 
    public async Task<PlanetMember> GetAuthorMemberAsync() => 
        await PlanetMember.FindAsync(Member_Id); 
        
    /// <summary> 
    /// Returns the author user of the message 
    /// </summary> 
    public async Task<User> GetAuthorUserAsync() => 
        await (await this.GetAuthorMemberAsync()).GetUserAsync(); 
 
    /// <summary> 
    /// Returns the planet the message was sent in 
    /// </summary> 
    public async Task<Planet> GetPlanetAsync() => 
        await Planet.FindAsync(Planet_Id); 

    /// <summary>
    /// Returns the channel the message was sent in
    /// </summary>
    public async Task<PlanetChatChannel> GetChannelAsync() =>
        await PlanetChatChannel.FindAsync(Channel_Id);

    /// <summary>
    /// Attempts to delete this message
    /// </summary>
    public async Task<TaskResult> DeleteAsync() =>
        await ValourClient.DeleteAsync($"api/channel/{Channel_Id}/messages/{Id}");

    /// <summary>
    /// Sends a message to the channel this message was sent in
    /// </summary>
    public async Task ReplyAsync(string text) =>
        await ValourClient.SendMessage(new(text, (await ValourClient.GetSelfMember(Planet_Id)).Id, Channel_Id, Planet_Id));

    /// <summary>
    /// Sends a message with a embed to the channel this message was sent in
    /// </summary>
    public async Task ReplyAsync(string text = "", Embed embed = null)
    {
        PlanetMessage message = new(text, (await ValourClient.GetSelfMember(Planet_Id)).Id, Channel_Id, Planet_Id);

        if (embed is not null)
        {
            JsonSerializerOptions options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
            message.EmbedData = JsonSerializer.Serialize(embed, options);
        }

        await ValourClient.SendMessage(message);
    }
}

