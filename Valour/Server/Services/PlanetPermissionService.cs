using System.Collections.Concurrent;
using System.Security.Cryptography;
using Valour.Sdk.ModelLogic.Exceptions;
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

// TODO: The caching of these could be saved to redis with sets. I'm not sure that the size of the caches will ever necessitate this, though.

public static class ChannelPermissionCache<TPermissionType>
    where TPermissionType : ChannelPermission
{
    private static readonly ConcurrentDictionary<long, long> _cache = new();

    public static long? GetChannelPermission(long key)
    {
        return _cache.GetValueOrDefault(key);
    }

    public static void SetChannelPermission(long key, long permission)
    {
        _cache[key] = permission;
    }
}

public static class ChannelAccessCache
{
    // Map from role key to channel ids they can access
    private static readonly ConcurrentDictionary<long, List<Channel>> _cache = new();

    public static List<Channel> GetChannelAccess(long roleKey)
    {
        return _cache.GetValueOrDefault(roleKey);
    }

    public static void SetChannelAccess(long roleKey, List<Channel> access)
    {
        _cache[roleKey] = access;
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
    private readonly HostedPlanetService _hostedPlanetService;
    
    public PlanetPermissionService(ValourDb db, HostedPlanetService hostedPlanetService)
    {
        _db = db;
        _hostedPlanetService = hostedPlanetService;
    }
    
    /// <summary>
    /// Returns all the distinct role combination keys that exist on the planet.
    /// This only returns combinations that are in use.
    /// </summary>
    public async Task<long[]> GetPlanetRoleComboKeysAsync(long planetId)
    {
        var distinctRoleKeys = await _db.PlanetMembers.Where(x => x.PlanetId == planetId)
            .Select(x => x.RoleHashKey)
            .Distinct()
            .ToArrayAsync();

        return distinctRoleKeys;
    }
    
    /// <summary>
    /// Returns all the distinct role combinations that exist on the planet.
    /// This only returns combinations that are in use.
    /// </summary>
    public async Task<Valour.Database.PlanetRole[][]> GetPlanetRoleCombosAsync(long planetId)
    {
        var roleCombos = await _db.PlanetMembers
            .Where(x => x.PlanetId == planetId)
            .Include(x => x.RoleMembership)
            .ThenInclude(y => y.Role)
            .GroupBy(x => x.RoleHashKey)
            .Select(g => g.FirstOrDefault())
            .Select(x => x.RoleMembership.Select(y => y.Role).ToArray())
            .ToArrayAsync();
        
        return roleCombos;
    }
    
    /// <summary>
    /// Returns a list of channels the member has access to
    /// </summary> 
    public ValueTask<List<Channel>> GetChannelAccessAsync(PlanetMember member) =>
        GetChannelAccessAsync(member.RoleHashKey);

    /// <summary>
    /// Returns a list of channels the specific combination of roles has access to
    /// </summary>
    public async ValueTask<List<Channel>> GetChannelAccessAsync(long roleKey)
    {
        var cached = ChannelAccessCache.GetChannelAccess(roleKey);
        if (cached is not null)
            return cached;
        
        // If not cached, generate the channel access
        var access = await GenerateChannelAccessAsync(roleKey);
        return access;
    }

    private async Task<List<Channel>> GenerateChannelAccessAsync(long roleKey)
    {
        // Pull the first member with this role key's roles
        var member = await _db.PlanetMembers
            .Include(x => x.RoleMembership)
            .FirstOrDefaultAsync(x => x.RoleHashKey == roleKey);

        var hostedPlanet = _hostedPlanetService.GetRequired(member.PlanetId);
        
        var roles = new List<PlanetRole>();
        foreach (var rm in member.RoleMembership)
        {
            if (!hostedPlanet.Roles.TryGet(rm.RoleId, out var role))
            {
                // This should never happen
                throw new Exception("Role not found in hosted planet roles!");  
            }
            
            roles.Add(role);
        }
        
        roles.Sort(ISortable.Comparer);

        List<Channel> access = new();
        
        foreach (var channel in hostedPlanet.Channels)
        {
            if (channel.IsDefault)
            {
                // Always have access to default channel
                access.Add(channel);
                continue;
            }

            long? perms = null;
            
            switch (channel.ChannelType)
            {
                case ChannelTypeEnum.PlanetChat:
                    perms = await GetChannelPermissionsAsync<ChatChannelPermission>(roleKey, roles, channel, hostedPlanet);
                    break;
                case ChannelTypeEnum.PlanetCategory:
                    perms = await GetChannelPermissionsAsync<CategoryPermission>(roleKey, roles, channel, hostedPlanet);
                    break;
                case ChannelTypeEnum.PlanetVoice:
                    perms = await GetChannelPermissionsAsync<VoiceChannelPermission>(roleKey, roles, channel, hostedPlanet);
                    break;
                default:
                    throw new Exception("Invalid channel type!");
            }
            
            // Check if the role has access to the channel
            if (Permission.HasPermission(perms.Value, ChannelPermissions.View))
            {
                access.Add(channel);
            }
        }
        
        // Cache
        ChannelAccessCache.SetChannelAccess(roleKey, access);
        
        return access;
    }
    
    public async ValueTask<long> GetChannelPermissionsAsync<TPermissionType>(
        long roleKey,
        List<PlanetRole> roles, 
        Channel channel,
        HostedPlanet hostedPlanet
    )
        where TPermissionType : ChannelPermission
    {
        // Try to get cached permissions
        var cachedPermissions = GetCachedChannelPermissionsAsync<TPermissionType>(roleKey, channel.Id);
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

        var channelKey = GetRoleChannelComboKey(roleKey, channel.Id);
        
        // Note for future self: The Owner role has IsAdmin, so this also checks for owner
        if (minRoles.Any(x => x.IsAdmin))
        {
            // Cache the permissions
            ChannelPermissionCache<TPermissionType>.SetChannelPermission(channelKey, Permission.FULL_CONTROL);
            
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
        ChannelPermissionCache<TPermissionType>.SetChannelPermission(channelKey, permissions);
        return permissions;
    }
    
    public long? GetCachedChannelPermissionsAsync<TPermissionType>(long roleKey, long channelId)
        where TPermissionType : ChannelPermission
    {
        // Get the combined key for the role ids and the channel id
        var channelKey = GetRoleChannelComboKey(roleKey, channelId);
        return ChannelPermissionCache<TPermissionType>.GetChannelPermission(channelKey);
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