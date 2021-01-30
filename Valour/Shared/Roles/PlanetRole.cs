using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Roles
{
    public class PlanetRole
    {
        [JsonIgnore]
        public static PlanetRole DefaultRole = new PlanetRole()
        {
            Name = "Default",
            Id = ulong.MaxValue,
            Authority = 0,
            Planet_Id = ulong.MaxValue
        };

        /// <summary>
        /// The unique Id of this role
        /// </summary>
        public ulong Id { get; set; }

        /// <summary>
        /// The name of the role
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The authority of this role: Higher authority is more powerful
        /// </summary>
        public int Authority { get; set; }

        /// <summary>
        /// The ID of the planet or system this role belongs to
        /// </summary>
        public ulong Planet_Id { get; set; }
    }
}
