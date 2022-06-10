using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Shared.Authorization;

namespace Valour.Database.Attributes
{
    public class PlanetPermsRequiredAttribute : Attribute
    {
        public string planetRouteName;
        public PlanetPermissionsEnum[] permissions;

        public PlanetPermsRequiredAttribute(string planetRouteName, params PlanetPermissionsEnum[] permissions)
        {
            this.planetRouteName = planetRouteName;
            this.permissions = permissions;
        }
    }
}
