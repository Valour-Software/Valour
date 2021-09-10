using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Valour.Shared.Oauth;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Roles
{
    public class PlanetRole
    {
        public static PlanetRole GetDefault(ulong planet_id)
        {
            return new PlanetRole()
            {
                Name = "Default",
                Id = ulong.MaxValue,
                Position = uint.MaxValue,
                Planet_Id = planet_id,
                Color_Red = 255,
                Color_Green = 255,
                Color_Blue = 255
            };
        }

        public static PlanetRole VictorRole = new PlanetRole()
        {
            Name = "Victor Class",
            Id = ulong.MaxValue,
            Position = uint.MaxValue,
            Planet_Id = 0,
            Color_Red = 255,
            Color_Green = 0,
            Color_Blue = 255
        };

        /// <summary>
        /// The unique Id of this role
        /// </summary>
        [JsonPropertyName("Id")]
        public ulong Id { get; set; }

        /// <summary>
        /// The name of the role
        /// </summary>
        [JsonPropertyName("Name")]
        public string Name { get; set; }

        /// <summary>
        /// The position of the role: Lower has more authority
        /// </summary>
        [JsonPropertyName("Position")]
        public uint Position { get; set; }

        /// <summary>
        /// The ID of the planet or system this role belongs to
        /// </summary>
        [JsonPropertyName("Planet_Id")]
        public ulong Planet_Id { get; set; }

        /// <summary>
        /// The planet permissions for the role
        /// </summary>
        [JsonPropertyName("Permissions")]
        public ulong Permissions { get; set; }

        // RGB Components for role color
        [JsonPropertyName("Color_Red")]
        public byte Color_Red { get; set; }

        [JsonPropertyName("Color_Green")]
        public byte Color_Green { get; set; }

        [JsonPropertyName("Color_Blue")]
        public byte Color_Blue { get; set; }

        // Formatting options
        [JsonPropertyName("Bold")]
        public bool Bold { get; set; }

        [JsonPropertyName("Italics")]
        public bool Italics { get; set; }

        public uint GetAuthority()
        {
            return uint.MaxValue - Position;
        }

        public Color GetColor()
        {
            return Color.FromArgb(Color_Red, Color_Green, Color_Blue);
        }

        public string GetColorHex()
        {
            Color c = GetColor();
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        public bool HasPermission(PlanetPermission perm)
        {
            return Permission.HasPermission(Permissions, perm);
        }
    }
}
