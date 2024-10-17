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
    public readonly long PlanetId;
    public readonly long TargetId;
    public readonly long RoleId;
    public readonly ChannelTypeEnum TargetType;
    
    public PermissionsNodeKey(long planetId, long targetId, long roleId, ChannelTypeEnum targetType)
    {
        TargetId = targetId;
        RoleId = roleId;
        TargetType = targetType;
        PlanetId = planetId;
    }
    
    public bool Equals(PermissionsNodeKey other)
    {
        return TargetId == other.TargetId && RoleId == other.RoleId && TargetType == other.TargetType && PlanetId == other.PlanetId;
    }
}

public class PermissionsNode : ClientPlanetModel<PermissionsNode, long>, ISharedPermissionsNode
{
    public override string IdRoute => ISharedPermissionsNode.GetIdRoute(this);
    public override string BaseRoute => ISharedPermissionsNode.BaseRoute;
    
    public static readonly Dictionary<PermissionsNodeKey, long> PermissionNodeIdLookup = new();
    
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

    public override long? GetPlanetId() => PlanetId;

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
    public PermissionsNodeKey GetCombinedKey() => new(PlanetId, TargetId, RoleId, TargetType);

    /// <summary>
    /// Returns the chat channel permissions node for the given channel and role
    /// </summary>
    public static ValueTask<PermissionsNode> FindAsync(Channel channel, PlanetRole role, ChannelTypeEnum targetType) =>
        FindAsync(new PermissionsNodeKey(role.PlanetId, channel.Id, role.Id, targetType));
    
    /// <summary>
    /// Returns the permissions node for the given values
    /// </summary>
    public static async ValueTask<PermissionsNode> FindAsync(PermissionsNodeKey key, bool refresh = false)
    {
        if (!refresh)
        {
            if (PermissionNodeIdLookup.TryGetValue(key, out var id))
            {
                var cached = Cache.Get(id);
                if (cached is not null)
                    return cached;
            }
        }

        var node = GetNodeForPlanet(key.PlanetId);
        var permNode = (await node.GetJsonAsync<PermissionsNode>(
            ISharedPermissionsNode.GetIdRoute(key.TargetId, key.RoleId, key.TargetType), 
            true)
        ).Data;
        
        if (permNode is not null)
            return await permNode.SyncAsync();

        return null;
    }
    

    public override PermissionsNode AddToCacheOrReturnExisting()
    {
        var existing = base.AddToCacheOrReturnExisting();
        
        // Add key to id lookup
        var key = GetCombinedKey();
        PermissionNodeIdLookup[key] = Id;   
        
        return existing;
    }

    public override PermissionsNode TakeAndRemoveFromCache()
    {
        // Remove key from id lookup
        var key = GetCombinedKey();
        PermissionNodeIdLookup.Remove(key);
        
        return base.TakeAndRemoveFromCache();
    }

    public static async Task<List<PermissionsNode>> GetAllForPlanetAsync(long planetId)
    {
        var node = GetNodeForPlanet(planetId);
        var permissionsNode = (await node.GetJsonAsync<List<PermissionsNode>>(
            ISharedPermissionsNode.GetAllRoute(planetId)
        )).Data;

        var results = new List<PermissionsNode>();
        
        foreach (var permNode in permissionsNode)
        {
            // Add or update in cache
            var cached = await permNode.SyncAsync();
            // Put cached node in results
            results.Add(cached);
        }
        
        // Return results
        return results;
    }
}

