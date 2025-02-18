using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2024 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public readonly struct PermissionsNodeKey : IEquatable<PermissionsNodeKey>
{
    public readonly long TargetId;
    public readonly long RoleId;
    public readonly ChannelTypeEnum TargetType;
    
    public PermissionsNodeKey(long targetId, long roleId, ChannelTypeEnum targetType)
    {
        TargetId = targetId;
        RoleId = roleId;
        TargetType = targetType;
    }
    
    public bool Equals(PermissionsNodeKey other)
    {
        return TargetId == other.TargetId && RoleId == other.RoleId && TargetType == other.TargetType;
    }
}

public class PermissionsNode : ClientPlanetModel<PermissionsNode, long>, ISharedPermissionsNode
{
    public override string IdRoute => ISharedPermissionsNode.GetIdRoute(this);
    public override string BaseRoute => ISharedPermissionsNode.BaseRoute;
    
    /// <summary>
    /// The planet this node belongs to
    /// </summary>
    public long PlanetId { get; set; }

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

    protected override long? GetPlanetId() => PlanetId;

    /// <summary>
    /// Returns the node code for this permission node
    /// </summary>
    public PermissionNodeCode GetNodeCode() =>
        ISharedPermissionsNode.GetNodeCode(this);

    /// <summary>
    /// Returns the permission state for a given permission
    /// </summary>
    public PermissionState GetPermissionState(Permission perm, bool ignoreviewperm = false) =>
        ISharedPermissionsNode.GetPermissionState(this, perm, ignoreviewperm);

    /// <summary>
    /// Sets a permission to the given state
    /// </summary>
    public void SetPermission(Permission perm, PermissionState state) =>
        ISharedPermissionsNode.SetPermission(this, perm, state);
    
    /// <summary>
    /// Returns a key used for caching nodes via several properties
    /// </summary>
    public PermissionsNodeKey GetCombinedKey() => new(TargetId, RoleId, TargetType);
    
    public override PermissionsNode AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        // Add key to id lookup
        var key = GetCombinedKey();
        Client.Cache.PermNodeKeyToId[key] = Id;

        return Planet.PermissionsNodes.Put(this, flags);
    }

    public override PermissionsNode RemoveFromCache(bool skipEvents = false)
    {
        // Remove key from id lookup
        var key = GetCombinedKey();
        
        Client.Cache.PermNodeKeyToId.Remove(key);

        return Planet.PermissionsNodes.Remove(this, skipEvents);
    }
}

