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

public class PlanetRole : ISharedPlanetRole, ISyncedItem<PlanetRole>, INodeSpecific
{
    #region Synced Item System

    /// <summary>
    /// Ran when this item is updated
    /// </summary>
    public event Func<int, Task> OnUpdated;

    /// <summary>
    /// Ran when this item is deleted
    /// </summary>
    public event Func<Task> OnDeleted;

    /// <summary>
    /// Run when any of this item type is updated
    /// </summary>
    public static event Func<PlanetRole, int, Task> OnAnyUpdated;

    /// <summary>
    /// Run when any of this item type is deleted
    /// </summary>
    public static event Func<PlanetRole, Task> OnAnyDeleted;

    public async Task InvokeAnyUpdated(PlanetRole updated, int flags)
    {
        if (OnAnyUpdated != null)
            await OnAnyUpdated?.Invoke(updated, flags);
    }

    public async Task InvokeAnyDeleted(PlanetRole deleted)
    {
        if (OnAnyDeleted != null)
            await OnAnyDeleted?.Invoke(deleted);
    }

    public async Task InvokeUpdated(int flags)
    {
        await OnUpdate(flags);

        if (OnUpdated != null)
            await OnUpdated?.Invoke(flags);
    }

    public async Task InvokeDeleted()
    {
        if (OnDeleted != null)
            await OnDeleted?.Invoke();
    }

    public async Task OnUpdate(int flags)
    {

    }

    #endregion

    /// <summary>
    /// The position of the role: Lower has more authority
    /// </summary>
    uint Position { get; set; }

    /// <summary>
    /// The ID of the planet or system this role belongs to
    /// </summary>
    ulong PlanetId { get; set; }

    /// <summary>
    /// The planet permissions for the role
    /// </summary>
    ulong Permissions { get; set; }

    // RGB Components for role color
    byte Red { get; set; }
    byte Green { get; set; }
    byte Blue { get; set; }

    // Formatting options
    bool Bold { get; set; }

    bool Italics { get; set; }

    public uint GetAuthority() =>
        ((ISharedPlanetRole)this).GetAuthority();

    public Color GetColor() =>
        ((ISharedPlanetRole)this).GetColor();

    public string GetColorHex() =>
        ((ISharedPlanetRole)this).GetColorHex();

    public bool HasPermission(PlanetPermission perm) =>
        ((ISharedPlanetRole)this).HasPermission(perm);

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
    
}
