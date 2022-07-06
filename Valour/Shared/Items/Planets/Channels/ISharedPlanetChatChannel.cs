using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Items.Planets.Channels;


/// <summary>
/// Represents a single chat channel within a planet
/// </summary>
public interface ISharedPlanetChatChannel : ISharedPlanetChannel
{
    long MessageCount { get; set; }
}

