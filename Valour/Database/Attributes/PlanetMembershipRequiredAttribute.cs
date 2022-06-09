using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Database.Attributes
{
    public class PlanetMembershipRequiredAttribute : Attribute
    {
        public string planetRouteName;

        public PlanetMembershipRequiredAttribute(string planetRouteName)
        {
            this.planetRouteName = planetRouteName;
        }
    }
}
