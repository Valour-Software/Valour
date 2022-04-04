using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Authorization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Items.Authorization;

/// <summary>
/// A permission node is a set of permissions for a specific thing
/// </summary>
public class PermissionsNodeBase : ISharedItem
{

    /// <summary>
    /// The permission code that this node has set
    /// </summary>
    [JsonPropertyName("Code")]
    public ulong Code { get; set; }

    /// <summary>
    /// A mask used to determine if code bits are disabled
    /// </summary>
    [JsonPropertyName("Mask")]
    public ulong Mask { get; set; }

    /// <summary>
    /// The planet this node applies to
    /// </summary>
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    /// <summary>
    /// The role this permissions node belongs to
    /// </summary>
    [JsonPropertyName("Role_Id")]
    public ulong Role_Id { get; set; }

    /// <summary>
    /// The id of the object this node applies to
    /// </summary>
    [JsonPropertyName("Target_Id")]
    public ulong Target_Id { get; set; }

    /// <summary>
    /// The type of object this node applies to
    /// </summary>
    [JsonPropertyName("Target_Type")]
    public ItemType Target_Type { get; set; }

    [NotMapped]
    [JsonInclude]
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.PermissionsNode;


    /// <summary>
    /// Returns the node code for this permission node
    /// </summary>
    public PermissionNodeCode GetNodeCode()
    {
        return new PermissionNodeCode(Code, Mask);
    }

    /// <summary>
    /// Returns the permission state for a given permission
    /// </summary>
    public PermissionState GetPermissionState(Permission perm)
    {
        return GetNodeCode().GetState(perm);
    }

    /// <summary>
    /// Sets a permission to the given state
    /// </summary>
    public void SetPermission(Permission perm, PermissionState state)
    {
        if (state == PermissionState.Undefined)
        {
            // Remove bit from code
            Code &= ~perm.Value;

            // Remove mask bit
            Mask &= ~perm.Value;
        }
        else if (state == PermissionState.True)
        {
            // Add mask bit
            Mask |= perm.Value;

            // Add true bit
            Code |= perm.Value;
        }
        else
        {
            // Remove mask bit
            Mask |= perm.Value;

            // Remove true bit
            Code &= ~perm.Value;
        }
    }
}

