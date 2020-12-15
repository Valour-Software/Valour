using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Shared.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Users
{
    /// <summary>
    /// Represents a user within a planet
    /// </summary>
    public class ClientPlanetUser : ClientUser
    {
        public string GetMainRoleColor()
        {
            return "#ff0000";
        }

        public List<PlanetRole> GetPlanetRoles()
        {
            // Implement later
            return null;
        }
    }
}
