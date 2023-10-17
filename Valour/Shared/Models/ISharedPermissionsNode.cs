using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Models;

public interface ISharedPermissionsTarget
{
    /// <summary>
    /// The type of target this item is
    /// </summary>
    public ChannelTypeEnum Type { get; }
}

/// <summary>
/// A permission node is a set of permissions for a specific thing
/// </summary>
public interface ISharedPermissionsNode : ISharedPlanetItem
{

    /// <summary>
    /// The permission code that this node has set
    /// </summary>
    public long Code { get; set; }

    /// <summary>
    /// A mask used to determine if code bits are disabled
    /// </summary>
    public long Mask { get; set; }

    /// <summary>
    /// The role this permissions node belongs to
    /// </summary>
    public long RoleId { get; set; }

    /// <summary>
    /// The id of the object this node applies to
    /// </summary>
    public long TargetId { get; set; }

    /// <summary>
    /// The type of object this node applies to
    /// </summary>
    public ChannelTypeEnum TargetType { get; set; }

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

    public static PermissionNodeCode GetNodeCode(ISharedPermissionsNode node) =>
        new PermissionNodeCode(node.Code, node.Mask);

    public static PermissionState GetPermissionState(ISharedPermissionsNode node, Permission perm, bool ignoreviewperm = false) =>
        node.GetNodeCode().GetState(perm, ignoreviewperm);

    public static void SetPermission(ISharedPermissionsNode node, Permission perm, PermissionState state)
    {
        if (state == PermissionState.Undefined)
        {
            // Remove bit from code
            node.Code &= ~perm.Value;

            // Remove mask bit
            node.Mask &= ~perm.Value;
        }
        else if (state == PermissionState.True)
        {
            // Add mask bit
            node.Mask |= perm.Value;

            // Add true bit
            node.Code |= perm.Value;
        }
        else
        {
            // Remove mask bit
            node.Mask |= perm.Value;

            // Remove true bit
            node.Code &= ~perm.Value;
        }
    }
}

