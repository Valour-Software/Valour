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

    [JsonPropertyName("ItemType")]
    public ItemType ItemType => ItemType.Role;

    public uint GetAuthority();

    public Color GetColor();

    public string GetColorHex();

    public bool HasPermission(PlanetPermission perm);
}

