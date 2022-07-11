using Valour.Shared.Items.Authorization;

namespace Valour.Shared.Items.Planets.Channels;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

/// <summary>
/// Represents a single chat Category within a planet
/// </summary>
public interface ISharedPlanetCategoryChannel : ISharedPlanetChannel, ISharedPermissionsTarget
{

}

