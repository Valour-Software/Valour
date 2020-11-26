using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Shared.Planets;

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
