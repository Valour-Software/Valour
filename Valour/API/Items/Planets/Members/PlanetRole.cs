using System.Drawing;
using Valour.Api.Client;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Planets.Members;

namespace Valour.Api.Items.Planets.Members;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetRole : SyncedItem<PlanetRole>, ISharedPlanetRole
{
    // Coolest role on this damn platform.
    // Fight me.
    public static PlanetRole VictorRole = new PlanetRole()
    {
        Name = "Victor Class",
        Id = ulong.MaxValue,
        Position = uint.MaxValue,
        PlanetId = 0,
        Red = 255,
        Green = 0,
        Blue = 255
    };

    /// <summary>
    /// The position of the role: Lower has more authority
    /// </summary>
    public uint Position { get; set; }

    /// <summary>
    /// The ID of the planet or system this role belongs to
    /// </summary>
    public ulong PlanetId { get; set; }

    /// <summary>
    /// The planet permissions for the role
    /// </summary>
    public ulong Permissions { get; set; }

    /// <summary>
    /// The name of this role
    /// </summary>
    public string Name { get; set; }

    // RGB Components for role color
    public byte Red { get; set; }
    public byte Green { get; set; }
    public byte Blue { get; set; }

    // Formatting options
    public bool Bold { get; set; }

    public bool Italics { get; set; }

    public uint GetAuthority() =>
        ISharedPlanetRole.GetAuthority(this);

    public Color GetColor() =>
        ISharedPlanetRole.GetColor(this);

    public string GetColorHex() =>
        ISharedPlanetRole.GetColorHex(this);

    public bool HasPermission(PlanetPermission perm) =>
        ISharedPlanetRole.HasPermission(this, perm);

    public static PlanetRole GetDefault(ulong planetId)
    {
        return new PlanetRole()
        {
            Name = "Default",
            Id = ulong.MaxValue,
            Position = uint.MaxValue,
            PlanetId = planetId,
            Red = 255,
            Green = 255,
            Blue = 255
        };
    }
}
