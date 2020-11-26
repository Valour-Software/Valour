using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Planets
{
    public class PlanetRole
    {
        /// <summary>
        /// The name of the role
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// The authority of this role: Higher authority is more powerful
        /// </summary>
        int Authority { get; set; }

        /// <summary>
        /// The ID of the planet this role belongs to
        /// </summary>
        public byte[] Planet_Id { get; set; }
    }
}
