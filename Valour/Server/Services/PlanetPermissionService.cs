using System.Collections.Concurrent;
using System.Security.Cryptography;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

// A note to those looking:
// The idea of using role combinations and sequential hashing to provide
// an extremely fast and low-storage alternative to traditional RBAC
// is a concept that I have been working on for a while. When I did the
// math and realized just how efficient it could be, I thought about
// monetizing this or patenting it to prevent a company like Discord
// from stealing it. But then I realized that this is a concept that
// should be free and open to everyone. So feel free to use this
// concept in your own projects, and if you want to credit me, that's
// cool too. And also mention Valour :)

// I'm going to also give this system the name HACKR-AUTH
// (HAshed Combined Role Keyed AUTHorization)
// because it sounds cool and I like acronyms.

// There is one slight downside: for a community with 100 roles, there
// is a 1 in 368 quadrillion chance of a hash collision. That's a risk
// I'm willing to take.

// - Spike, 2024

public static class PermissionCache<TPermissionType>
    where TPermissionType : ChannelPermission
{
    private static readonly ConcurrentDictionary<long, long> _cache = new();

    public static long? GetChannelPermission(long key)
    {
        return _cache.TryGetValue(key, out var permission) ? permission : null;
    }

    public static void SetChannelPermission(long key, long permission)
    {
        _cache[key] = permission;
    }
}

public struct MinimalRoleInfo
{
    public bool IsAdmin { get; set; }
    public long Id { get; set; }
}

/// <summary>
/// Provides methods for checking and enforcing permissions in planets
/// </summary>
public class PlanetPermissionService
{
    private const long Seed = unchecked((long)0xcbf29ce484222325); // Use unchecked to safely cast the ulong seed to long
    private const long MagicNumber = unchecked((long)0x9e3779b97f4a7c15);
    
    private readonly ValourDb _db;
    
    public PlanetPermissionService(ValourDb db)
    {
        _db = db;
    }
    
    public async ValueTask<long> GetChannelPermissionsAsync<TPermissionType>(
        ISharedPlanetMember member, 
        ISharedChannel channel
    )
        where TPermissionType : ChannelPermission
    {
        // Planet owners have full control
        if (member.IsPlanetOwner)
        {
            return Permission.FULL_CONTROL;
        }
        
        // Try to get cached permissions
        var cachedPermissions = GetCachedChannelPermissionsAsync<TPermissionType>(member.RoleHashKey, channel.Id);
        if (cachedPermissions != null)
        {
            return cachedPermissions.Value;
        }
        
        // Handle channel inheritance
        // TODO: use magic logic via new channel position system
        while (channel.InheritsPerms && channel.ParentId is not null)
        {
            var parent = await _db.Channels.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == channel.ParentId);

            if (parent is null)
                break;
            
            // Switch to parent scope
            channel = parent;
        }
        
        // Pull the member's minimal role info from the database
        var minRoles = await _db.PlanetRoleMembers.AsNoTracking()
            .Include(x => x.Role)
            .OrderBy(x => x.Role.Position)
            .Select(x =>
                new MinimalRoleInfo() {
                    IsAdmin = x.Role.IsAdmin,
                    Id = x.Role.Id
                })
            .ToListAsync();

        var roleKey = GenerateRoleComboKey(minRoles);
        var channelKey = GetRoleChannelComboKey(roleKey, channel.Id);
        
        if (minRoles.Any(x => x.IsAdmin))
        {
            // Cache the permissions
            PermissionCache<TPermissionType>.SetChannelPermission(channelKey, Permission.FULL_CONTROL);
            
            return Permission.FULL_CONTROL;
        }

        var targetType = ISharedPermissionsNode.GetChannelTypeEnum<TPermissionType>();

        var targetRoleIds = new long[minRoles.Count];
        for (int i = 0; i < minRoles.Count; i++)
        {
            targetRoleIds[i] = minRoles[i].Id;
        }
        
        // Use role ids to pull permissions nodes
        var permNodes = await _db.PermissionsNodes.AsNoTracking()
            .Where(x =>
                x.TargetType == targetType &&
                x.TargetId == channel.Id &&
                targetRoleIds.Contains(x.RoleId))
            // We want the role order to be from weakest to strongest
            .OrderByDescending(x => x.Role.Position)
            .ToListAsync();

        if (permNodes.Count == 0)
        {
            // Something is wrong
            throw new Exception("No permissions nodes found for permission check!");
        }
        
        long permissions = 0; // Start with no permissions

        // Assuming permNodes is ordered from weakest to strongest:
        foreach (var node in permNodes)
        {
            // Clear the bits that this node's mask controls
            permissions &= ~node.Mask;

            // Set the bits according to the node's code
            permissions |= (node.Code & node.Mask);
        }
        
        // Cache the permissions
        PermissionCache<TPermissionType>.SetChannelPermission(channelKey, permissions);
        return permissions;
    }
    
    public long? GetCachedChannelPermissionsAsync<TPermissionType>(long roleKey, long channelId)
        where TPermissionType : ChannelPermission
    {
        // Get the combined key for the role ids and the channel id
        var channelKey = GetRoleChannelComboKey(roleKey, channelId);
        return PermissionCache<TPermissionType>.GetChannelPermission(channelKey);
    }

    // A simple mix function that combines the previous hash with the next to create a new unique hash
    private long MixHash(long currentHash, long roleId)
    {
        // XOR mixing for a simple and fast hash
        return currentHash ^ ((roleId + MagicNumber) + (currentHash << 6) + (currentHash >> 2));
    }
    
    /// <summary>
    /// Returns a combined key for a given set of role IDs and a channel ID.
    /// </summary>
    private long GetRoleChannelComboKey(long rolesKey, long channelId)
    {
        // Step 1: Get hash for the channel ID
        var hash = MixHash(Seed, channelId);
        
        // Step 2: Mix the role combo key with the channel ID
        hash = MixHash(rolesKey, hash);
        
        // Step 3: Return the final hash value representing the combination of roles and channels
        return hash;
    }

    /// <summary>
    /// Returns the combined hash key for the given role ids. Unique for any combination of roles.
    /// You MUST provide a sorted list of role IDs.
    /// </summary>
    public long GenerateRoleComboKey(long[] sortedRoleIds)
    {
        var hash = Seed; // Initial value (seed value)

        foreach (long roleId in sortedRoleIds)
        {
            // Step 1: Mix the current hash with the role ID sequentially
            hash = MixHash(hash, roleId);
        }

        // Step 2: Return the final hash value representing the combination of roles
        return hash;
    }
    
    /// <summary>
    /// Returns the combined hash key for the given roles. Unique for any combination of roles.
    /// You MUST provide a sorted list of roles.
    /// </summary>
    public long GenerateRoleComboKey(List<MinimalRoleInfo> sortedRoles)
    {
        var hash = Seed; // Initial value (seed value)

        foreach (var role in sortedRoles)
        {
            // Step 1: Mix the current hash with the role ID sequentially
            hash = MixHash(hash, role.Id);
        }

        // Step 2: Return the final hash value representing the combination of roles
        return hash;
    }
    
}