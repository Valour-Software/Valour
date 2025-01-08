using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.ObjectPool;
using Valour.Server.Utilities;
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
    
    public static readonly ObjectPool<List<PlanetRole>> RoleListPool = 
        new DefaultObjectPool<List<PlanetRole>>(new ListPooledObjectPolicy<PlanetRole>());
    
    private readonly ValourDb _db;
    private readonly HostedPlanetService _hostedPlanetService;
    
    public PlanetPermissionService(ValourDb db, HostedPlanetService hostedPlanetService)
    {
        _db = db;
        _hostedPlanetService = hostedPlanetService;
    }
    
    public async ValueTask<bool> HasPlanetPermissionAsync(PlanetMember member, PlanetPermission permission)
    {
        var permissions = await GetPlanetPermissionsAsync(member);
        return Permission.HasPermission(permissions, permission);
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
    /// Updates access and permissions for all combinations with the given role
    /// </summary>
    public async Task HandleRoleChange(PlanetRole role)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(role.PlanetId);
        
        // Get all combinations in use that contain this role
        var roleCombos = await _db.PlanetMembers
            .Where(x => x.RoleMembership.Any(y => y.RoleId == role.Id))
            .Select(x => x.RoleHashKey)
            .Distinct()
            .ToArrayAsync();
        
        // Clear all cached channel accesses and permissions for these role combos
        foreach (var roleKey in roleCombos)
        {
            hostedPlanet.PermissionCache.ClearCacheForCombo(roleKey);
        }
    }

    /// <summary>
    /// Used whenever a member's roles change to update their role hash key
    /// </summary>
    /// <param name="memberId">The member to update</param>
    public async Task UpdateMemberRoleHashAsync(long memberId)
    {
        var member = await _db.PlanetMembers.FindAsync(memberId);
        if (member is null)
            return;
        
        var roleIds = await _db.PlanetRoleMembers
            .Where(x => x.MemberId == memberId)
            .Select(x => x.RoleId)
            .OrderBy(x => x)
            .ToArrayAsync();
        
        var roleKey = GenerateRoleComboKey(roleIds);
        member.RoleHashKey = roleKey;
        
        await _db.SaveChangesAsync();
    }
    
    /// <summary>
    /// Returns if the member has access to the given channel
    /// </summary> 
    public async ValueTask<bool> HasChannelAccessAsync(PlanetMember member, long channelId)
    {
        var access = await GetChannelAccessAsync(member);
        return access.Contains(channelId);
    }

    public async ValueTask<uint> GetAuthorityAsync(PlanetMember member)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(member.PlanetId);
        var cached = hostedPlanet.PermissionCache.GetAuthority(member.RoleHashKey);
        
        if (cached is not null)
            return cached.Value;
        
        // What we calculate here applies to everyone with this role combination
        var rolePos = await _db.PlanetRoleMembers
            .AsNoTracking()
            .Where(x => x.MemberId == member.Id)
            .Include(x => x.Role)
            .OrderBy(x => x.Role.Position)
            .Select(x => x.Role.Position)
            .FirstAsync();

        var authority = uint.MaxValue - rolePos;
        
        hostedPlanet.PermissionCache.SetAuthority(member.RoleHashKey, authority);
        
        return authority;
    }
    
    /// <summary>
    /// Returns a list of channels the member has access to
    /// </summary> 
    public async ValueTask<SortedServerModelList<Channel, long>> GetChannelAccessAsync(PlanetMember member)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(member.PlanetId);
        var cached = hostedPlanet.PermissionCache.GetChannelAccess(member.RoleHashKey);
        if (cached is not null)
            return cached;
        
        // If not cached, generate the channel access
        var access = await GenerateChannelAccessAsync(member);
        
        return access;
    }

    private async Task<SortedServerModelList<Channel, long>> GenerateChannelAccessAsync(PlanetMember member)
    {
        // The member acts as representative for the role combination
        
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(member.PlanetId);

        var roleMembership = await _db.PlanetRoleMembers
            .Where(x => x.MemberId == member.Id)
            .Select(x => x.RoleId)
            .ToListAsync();
        
        var allChannels = hostedPlanet.GetChannels();
        
        var roles = RoleListPool.Get();

        var isAdmin = false;
        
        foreach (var roleId in roleMembership)
        {
            var role = hostedPlanet.GetRole(roleId);
            if (role is null)
            {
                // This should never happen
                throw new Exception("Role not found in hosted planet roles!");  
            }
            
            // If admin, they can access all channels
            if (role.IsAdmin)
            {
                isAdmin = true;
                break;
            }
            
            roles.Add(role);
        }
        
        if (isAdmin)
        {
            // Cache
            var adminResult = hostedPlanet.PermissionCache.SetChannelAccess(member.RoleHashKey, allChannels);
            
            // cleanup
            RoleListPool.Return(roles);
            hostedPlanet.RecycleChannelList(allChannels);
            
            return adminResult;
        }
        
        roles.Sort(ISortable.Comparer);

        var access = hostedPlanet.PermissionCache.GetEmptyAccessList();
        
        foreach (var channel in allChannels)
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
                    perms = await GenerateChannelPermissionsAsync<ChatChannelPermission>(member.RoleHashKey, roles, channel, hostedPlanet);
                    break;
                case ChannelTypeEnum.PlanetCategory:
                    perms = await GenerateChannelPermissionsAsync<CategoryPermission>(member.RoleHashKey, roles, channel, hostedPlanet);
                    break;
                case ChannelTypeEnum.PlanetVoice:
                    perms = await GenerateChannelPermissionsAsync<VoiceChannelPermission>(member.RoleHashKey, roles, channel, hostedPlanet);
                    break;
                default:
                    throw new Exception("Invalid channel type!");
            } 
            
            // Check if the role has access to view the channel
            if (Permission.HasPermission(perms.Value, ChannelPermissions.View))
            {
                access.Add(channel);
            }
        }
        
        // eco-friendly
        hostedPlanet.RecycleChannelList(allChannels);
        RoleListPool.Return(roles);
        // AccessListPool.Return(access); handled by cache
        
        // Cache
        var result = hostedPlanet.PermissionCache.SetChannelAccess(member.RoleHashKey, access);
        
        return result;
    }

    private async ValueTask<long> GetPlanetPermissionsAsync(PlanetMember member)
    {
        // The member acts as representative for the role combination
        
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(member.PlanetId);
        
        // try to get cached
        var cached = hostedPlanet.PermissionCache.GetPlanetPermissions(member.RoleHashKey);
        if (cached is not null)
            return cached.Value;
        
        var roleMembership = await _db.PlanetRoleMembers
            .Where(x => x.MemberId == member.Id)
            .Select(x => x.RoleId)
            .ToListAsync();
        
        long permissions = 0;
        
        // Easy to calculate: OR together all permissions.
        // Unlike channel permissions, planet permissions can only be additive.
        foreach (var roleId in roleMembership)
        {
            var role = hostedPlanet.GetRole(roleId);
            if (role is null)
            {
                // This should never happen
                throw new Exception("Role not found in hosted planet roles!");  
            }
            
            // If admin, they can access all channels
            if (role.IsAdmin)
            {
                permissions = Permission.FULL_CONTROL;
                break;
            }
            
            permissions |= role.Permissions;
        }
        
        // Cache
        hostedPlanet.PermissionCache.SetPlanetPermissions(member.RoleHashKey, permissions);
        
        return permissions;
    }
    
    public async ValueTask<bool> HasChannelPermissionAsync(PlanetMember member, Channel channel, ChannelPermission permission)
    {
        var permissions = await GetChannelPermissionsAsync(member, channel, permission);
        return Permission.HasPermission(permissions, permission);
    }

    public async ValueTask<long> GetChannelPermissionsAsync(PlanetMember member, Channel channel, ChannelPermission permission)
    {
        var channelKey = GetRoleChannelComboKey(member.RoleHashKey, channel.Id);
        
        // Check if cached
        var hosted = await _hostedPlanetService.GetRequiredAsync(member.PlanetId);
        var cache = hosted.PermissionCache.GetChannelCache(channel.ChannelType);
        var cachedPermissions = cache.GetChannelPermission(channelKey);
        
        if (cachedPermissions is not null)
            return cachedPermissions.Value;
        
        // If not cached, generate the permissions
        var roleMembership = await _db.PlanetRoleMembers
            .Where(x => x.MemberId == member.Id)
            .Select(x => x.RoleId)
            .ToListAsync();
        
        var roles = RoleListPool.Get();

        foreach (var roleId in roleMembership)
        {
            var role = hosted.GetRole(roleId);
            if (role is null)
            {
                // This should never happen
                throw new Exception("Role not found in hosted planet roles!");  
            }
            
            roles.Add(role);
        }
        
        var permissions = await GenerateChannelPermissionsAsync<ChannelPermission>(member.RoleHashKey, roles, channel, hosted);
        
        // Recycle
        RoleListPool.Return(roles);
        
        // Cache
        cache.Set(member.RoleHashKey, channelKey, permissions);
        
        return permissions;
    }
    
    private async ValueTask<long> GenerateChannelPermissionsAsync<TPermissionType>(
        long roleKey,
        List<PlanetRole> roles, 
        Channel channel,
        HostedPlanet hostedPlanet
    )
        where TPermissionType : ChannelPermission
    {
        var channelKey = GetRoleChannelComboKey(roleKey, channel.Id);
        
        var permCache = hostedPlanet.PermissionCache.GetChannelCache(channel.ChannelType);
        
        // Try to get cached permissions
        var cachedPermissions = permCache.GetChannelPermission(channelKey);
        if (cachedPermissions != null)
        {
            return cachedPermissions.Value;
        }
        
        // Handle channel inheritance
        while (channel.InheritsPerms && channel.ParentId is not null)
        {
            var parent = hostedPlanet.GetChannel(channel.ParentId.Value);
            if (parent is null)
                break;
            
            // Switch to parent scope
            channel = parent;
        }
        
        // Note for future self: The Owner role has IsAdmin, so this also checks for owner
        if (roles.Any(x => x.IsAdmin))
        {
            permCache.Set(roleKey, channelKey, Permission.FULL_CONTROL);
            return Permission.FULL_CONTROL;
        }

        var targetType = ISharedPermissionsNode.GetChannelTypeEnum<TPermissionType>();

        var targetRoleIds = new long[roles.Count];
        for (int i = 0; i < roles.Count; i++)
        {
            targetRoleIds[i] = roles[i].Id;
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
        permCache.Set(roleKey, channelKey, permissions);
        
        return permissions;
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

    public long GenerateRoleComboKey(IEnumerable<PlanetRole> roles)
    {
        var hash = Seed;
        
        foreach (var role in roles.OrderBy(x => x.Id))
        {
            hash = MixHash(hash, role.Id);
        }
        
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
}