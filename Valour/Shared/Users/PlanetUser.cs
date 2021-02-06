using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Shared.Planets;
using Valour.Shared.Roles;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Users
{
    /// <summary>
    /// Represents a user within a planet
    /// </summary>
    public class PlanetUser : User
    {
        public ulong Planet_Id { get; set; }

        public string GetState()
        {
            return "Currently browsing";
        }

        public string GetMainRoleColor()
        {
            return "#00FAFF";
        }

        public List<PlanetRole> GetPlanetRoles()
        {
            // Implement later
            return null;
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
