using System.Drawing;
using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets.Members;

namespace Valour.Api.Items.Planets.Members;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetRole : NamedItem<PlanetRole>, ISharedPlanetRole
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
    public override ItemType ItemType => ItemType.PlanetRole;

    /// <summary>
    /// Returns the planet role for the given id
    /// </summary>
    public static async Task<PlanetRole> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<PlanetRole>(id);
            if (cached is not null)
                return cached;
        }

        var role = await ValourClient.GetJsonAsync<PlanetRole>($"api/role/{id}");

        if (role is not null)
            await ValourCache.Put(id, role);

        return role;
    }

    public uint GetAuthority() => 
        ((ISharedPlanetRole)this).GetAuthority();


    public Color GetColor() =>
        ((ISharedPlanetRole)this).GetColor();
    

    public string GetColorHex() =>
        ((ISharedPlanetRole)this).GetColorHex();


    public bool HasPermission(PlanetPermission perm) =>
        ((ISharedPlanetRole)this).HasPermission(perm);
    
}
