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
public interface ISharedPlanetCategoryChannel
{
    // Inherited from ISharedPlanetChannel
    ulong PlanetId { get; set; }
    ulong? ParentId { get; set; }

    // Inherited from ISharedChannel
    ulong Id { get; set; }
    string Name { get; set; }
    int Position { get; set; }
    string Description { get; set; }
}

