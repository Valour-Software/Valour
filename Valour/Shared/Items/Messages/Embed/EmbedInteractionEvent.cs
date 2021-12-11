using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Messages.Embeds;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class EmbedInteractionEvent
{
    [JsonPropertyName("Event")]
    public string Event { get; set; }

    [JsonPropertyName("Element_Id")]
    public string Element_Id { get; set; }

    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    [JsonPropertyName("Message_Id")]
    public ulong Message_Id { get; set; }

    [JsonPropertyName("Author_Member_Id")]
    public ulong Author_Member_Id { get; set; }

    [JsonPropertyName("Member_Id")]
    public ulong Member_Id { get; set; }

    [JsonPropertyName("Channel_Id")]
    public ulong Channel_Id { get; set; }

    [JsonPropertyName("Time_Interacted")]
    public DateTime Time_Interacted { get; set; }

    [JsonPropertyName("Form_Data")]
    public List<EmbedFormData> Form_Data { get; set; }
}

