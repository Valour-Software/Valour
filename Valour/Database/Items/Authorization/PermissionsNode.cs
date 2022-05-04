using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items.Planets;
using Valour.Database.Items.Planets.Channels;
using Valour.Database.Items.Planets.Members;
using Valour.Shared.Authorization;
using Valour.Shared.Items;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Planets.Channels;

namespace Valour.Database.Items.Authorization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PermissionsNode : ISharedPermissionsNode
{
    [ForeignKey("Planet_Id")]
    [JsonIgnore]
    public virtual Planet Planet { get; set; }

    [ForeignKey("Role_Id")]
    [JsonIgnore]
    public virtual PlanetRole Role { get; set; }

    /// <summary>
    /// The permission code that this node has set
    /// </summary>
    public ulong Code { get; set; }

    /// <summary>
    /// A mask used to determine if code bits are disabled
    /// </summary>
    public ulong Mask { get; set; }

    /// <summary>
    /// The planet this node applies to
    /// </summary>
    public ulong Planet_Id { get; set; }

    /// <summary>
    /// The role this permissions node belongs to
    /// </summary>
    public ulong Role_Id { get; set; }

    /// <summary>
    /// The id of the object this node applies to
    /// </summary>
    public ulong Target_Id { get; set; }

    /// <summary>
    /// The type of object this node applies to
    /// </summary>
    public ItemType Target_Type { get; set; }

    /// <summary>
    /// Returns the node code for this permission node
    /// </summary>
    public PermissionNodeCode GetNodeCode() =>
        ISharedPermissionsNode.GetNodeCode(this);

    /// <summary>
    /// Returns the permission state for a given permission
    /// </summary>
    public PermissionState GetPermissionState(Permission perm) =>
        ISharedPermissionsNode.GetPermissionState(this, perm);

    /// <summary>
    /// Sets a permission to the given state
    /// </summary>
    public void SetPermission(Permission perm, PermissionState state) =>
        ISharedPermissionsNode.SetPermission(this, perm, state);

    /// <summary>
    /// Returns the target of this permissions node
    /// </summary>

    public async Task<PlanetChannel> GetTargetAsync(ValourDB db)
        => await db.PlanetChannels.FindAsync(Target_Id);
}

