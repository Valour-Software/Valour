using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
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
        public uint Authority { get; set; }

        /// <summary>
        /// The ID of the planet or system this role belongs to
        /// </summary>
        public ulong Planet_Id { get; set; }

        // RGB Components for role color
        public byte Color_Red { get; set; }

        public byte Color_Green { get; set; }

        public byte Color_Blue { get; set; }

        public Color GetColor()
        {
            return Color.FromArgb(Color_Red, Color_Green, Color_Blue);
        }

        public string GetColorHex()
        {
            Color c = GetColor();
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }
    }
}
