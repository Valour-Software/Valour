using System.ComponentModel.DataAnnotations.Schema;
using System.Drawing;
using System.Text.Json.Serialization;
using Valour.Shared.Authorization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Items.Planets.Members;

public interface PlanetRoleBase
{
    /// <summary>
    /// The position of the role: Lower has more authority
    /// </summary>
    uint Position { get; set; }

    /// <summary>
    /// The ID of the planet or system this role belongs to
    /// </summary>
    ulong Planet_Id { get; set; }

    /// <summary>
    /// The planet permissions for the role
    /// </summary>
    ulong Permissions { get; set; }

    // RGB Components for role color
    byte Color_Red { get; set; }
    byte Color_Green { get; set; }
    byte Color_Blue { get; set; }

    // Formatting options
    bool Bold { get; set; }

    bool Italics { get; set; }

    public uint GetAuthority() => 
        uint.MaxValue - Position;

    public Color GetColor() => 
        Color.FromArgb(Color_Red, Color_Green, Color_Blue);

    public string GetColorHex()
    {
        Color c = GetColor();
        return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
    }

    public bool HasPermission(PlanetPermission perm) =>
        Permission.HasPermission(Permissions, perm);
}

