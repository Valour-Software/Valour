/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Planets.Channels;

/// <summary>
/// Represents a single chat Category within a planet
/// </summary>
public class PlanetCategoryBase : PlanetChannel
{
    /// <summary>
    /// The item type of this item
    /// </summary>
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.Category;
}

