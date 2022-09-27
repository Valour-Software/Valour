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

public interface ISharedPlanetRole : ISharedPlanetItem
{
    /// <summary>
    /// The position of the role: Lower has more authority
    /// </summary>
    int Position { get; set; }

    /// <summary>
    /// The planet permissions for the role
    /// </summary>
    long Permissions { get; set; }

    // RGB Components for role color
    byte Red { get; set; }
    byte Green { get; set; }
    byte Blue { get; set; }

    // Formatting options
    bool Bold { get; set; }

    bool Italics { get; set; }

    public int GetAuthority() =>
        ISharedPlanetRole.GetAuthority(this);

    public Color GetColor() =>
        ISharedPlanetRole.GetColor(this);


    public string GetColorHex() =>
        ISharedPlanetRole.GetColorHex(this);

    public bool HasPermission(PlanetPermission perm) =>
        ISharedPlanetRole.HasPermission(this, perm);

    public static int GetAuthority(ISharedPlanetRole role) =>
        int.MaxValue - role.Position - 1; // Subtract one so owner can have higher

    public static Color GetColor(ISharedPlanetRole role) =>
        Color.FromArgb(role.Red, role.Green, role.Blue);

    public static string GetColorHex(ISharedPlanetRole role)
    {
        Color c = role.GetColor();
        return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
    }

    public static bool HasPermission(ISharedPlanetRole role, PlanetPermission perm)
        => Permission.HasPermission(role.Permissions, perm);

}

