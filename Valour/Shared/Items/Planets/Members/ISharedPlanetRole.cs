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

public interface ISharedPlanetRole
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
        ISharedPlanetRole.GetAuthority(this);

    public Color GetColor() =>
        ISharedPlanetRole.GetColor(this);


    public string GetColorHex() =>
        ISharedPlanetRole.GetColorHex(this);

    public bool HasPermission(PlanetPermission perm) =>
        ISharedPlanetRole.HasPermission(this, perm);

    public static uint GetAuthority(ISharedPlanetRole role) =>
        uint.MaxValue - role.Position;

    public static Color GetColor(ISharedPlanetRole role) =>
        Color.FromArgb(role.Color_Red, role.Color_Green, role.Color_Blue);

    public static string GetColorHex(ISharedPlanetRole role)
    {
        Color c = role.GetColor();
        return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
    }

    public static bool HasPermission(ISharedPlanetRole role, PlanetPermission perm)
        => Permission.HasPermission(role.Permissions, perm);

}

