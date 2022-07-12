using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Messages.Embeds;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public enum EmbedIteractionEventType
{
    ButtonClick = 1
}

public class EmbedInteractionEvent
{
    [JsonPropertyName("Event")]
    public string Event { get; set; }

    [JsonPropertyName("EmbedIteractionEventType")]
    public EmbedIteractionEventType EventType { get; set; } 

    [JsonPropertyName("Element_Id")]
    public string Element_Id { get; set; }

    [JsonPropertyName("PlanetId")]
    public long PlanetId { get; set; }

    [JsonPropertyName("Message_Id")]
    public long Message_Id { get; set; }

    [JsonPropertyName("Author_MemberId")]
    public long Author_MemberId { get; set; }

    [JsonPropertyName("MemberId")]
    public long MemberId { get; set; }

    [JsonPropertyName("ChannelId")]
    public long ChannelId { get; set; }

    [JsonPropertyName("Time_Interacted")]
    public DateTime Time_Interacted { get; set; }

    [JsonPropertyName("Form_Data")]
    public List<EmbedFormData> Form_Data { get; set; }
}

