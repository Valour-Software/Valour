using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Items;
using Valour.Shared.Items.Messages;

namespace Valour.Database.Items.Messages;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PlanetMessage : Message, ISharedPlanetMessage
{
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    [NotMapped]
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.PlanetMessage;
}

