using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items.Planets;
using Valour.Database.Items.Planets.Channels;
using Valour.Database.Items.Planets.Members;
using Valour.Shared.Authorization;
using Valour.Shared.Items;
using Valour.Shared.Items.Authorization;

namespace Valour.Database.Items.Authorization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PermissionsNode : Item, ISharedPermissionsNode
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
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.PermissionsNode;


    /// <summary>
    /// Returns the node code for this permission node
    /// </summary>
    public PermissionNodeCode GetNodeCode() =>
        ((ISharedPermissionsNode)this).GetNodeCode();

    /// <summary>
    /// Returns the permission state for a given permission
    /// </summary>
    public PermissionState GetPermissionState(Permission perm) =>
        ((ISharedPermissionsNode)this).GetPermissionState(perm);


    /// <summary>
    /// Sets a permission to the given state
    /// </summary>
    public void SetPermission(Permission perm, PermissionState state) =>
        ((ISharedPermissionsNode)this).SetPermission(perm, state);

/// <summary>
/// This is a somewhat dirty way to fix the problem,
/// but I need more time to figure out how to escape the generics hell
/// i have created - spikey boy
/// </summary>

public async Task<PlanetChannel> GetTargetAsync(ValourDB db)
    {
        switch (Target_Type)
        {
            case ItemType.ChatChannel: return await db.PlanetChatChannels.FindAsync(Target_Id);
            case ItemType.Category: return await db.PlanetCategories.FindAsync(Target_Id);
        }

        return null;
    }
}
