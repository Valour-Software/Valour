using System.Text.Json.Serialization;
using Valour.Api.Items.Messages.Embeds.Items;

namespace Valour.Api.Items.Messages.Embeds;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class EmbedFormData
{
    [JsonPropertyName("ElementId")]
    public string ElementId { get; set; }

    [JsonPropertyName("Value")]
    public string Value { get; set; }

    [JsonPropertyName("Type")]
    public EmbedItemType Type { get; set; }
}