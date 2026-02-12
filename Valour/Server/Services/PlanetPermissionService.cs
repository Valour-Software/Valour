#nullable enable

using Microsoft.Extensions.ObjectPool;
using Valour.Server.Utilities;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

// I'm going to give this system the name HACKR-AUTH
// (HAshed Combined Role Keyed AUTHorization)
// because it sounds cool and I like acronyms.

// There is one slight downside: for a community with 100 roles, there
// is a 1 in 368 quadrillion chance of a hash collision. That's a risk
// I'm willing to take.

// - Spike, 2024

public class PlanetPermissionService
{
    public static readonly ObjectPool<List<PlanetRole>> RoleListPool =
        new DefaultObjectPool<List<PlanetRole>>(new ListPooledObjectPolicy<PlanetRole>());

    private readonly ValourDb _db;
    private readonly HostedPlanetService _hostedPlanetService;
    private readonly CoreHubService _coreHub;
    private readonly ILogger<PlanetPermissionService> _logger;

    public PlanetPermissionService(ValourDb db, HostedPlanetService hostedPlanetService,
        ILogger<PlanetPermissionService> logger, CoreHubService coreHub)
    {
        _db = db;
        _hostedPlanetService = hostedPlanetService;
        _logger = logger;
        _coreHub = coreHub;
    }

    /// <summary>
    /// Returns all the distinct role combination keys that exist on the planet.
    /// This only returns combinations that are in use.
    /// </summary>
    public async Task<PlanetRoleMembership[]> GetAllUniqueRoleCombinationsForPlanet(long planetId)
    {
        return await _db.PlanetMembers
            .Where(x => x.PlanetId == planetId)
            .Select(x => x.RoleMembership)
            .Distinct()
            .ToArrayAsync();
    }

    /// <summary>
    /// Returns all the distinct role combinations that exist on the planet.
    /// This only returns combinations that are in use.
    /// </summary>
    public async Task<PlanetRole[][]> GetPlanetRoleCombosAsync(long planetId)
    {
        var combos = await GetAllUniqueRoleCombinationsForPlanet(planetId);

        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);

        var roleArrs = new PlanetRole[combos.Length][];
        var roleList = RoleListPool.Get();

        try
        {
            for (int i = 0; i < combos.Length; i++)
            {
                var combo = combos[i];
                roleList.Clear();

                foreach (var roleIndex in combo.EnumerateRoleIndices())
                {
                    var role = hostedPlanet.GetRoleByIndex(roleIndex);
                    if (role is null)
                    {
                        _logger.LogWarning("Role not found for role index {RoleIndex} in planet {PlanetId}", roleIndex, planetId);
                        continue;
                    }

                    roleList.Add(role);
                }

                roleArrs[i] = roleList.ToArray();
            }
        }
        finally
        {
            RoleListPool.Return(roleList);
        }

        return roleArrs;
    }

    public async ValueTask<bool> HasPlanetPermissionAsync(long memberId, PlanetPermission permission)
    {
        var member = await _db.PlanetMembers.FindAsync(memberId);
        if (member is null)
            return false;

        // Special case: everyone can view.
        if (permission.Value == PlanetPermissions.View.Value)
            return true;

        var perms = await GetPlanetPermissionsAsync(member);
        return Permission.HasPermission(perms, permission);
    }

    public async ValueTask<bool> HasPlanetPermissionAsync(ISharedPlanetMember member, PlanetPermission permission)
    {
        if (member is null)
            return false;

        if (permission.Value == PlanetPermissions.View.Value)
            return true;

        var perms = await GetPlanetPermissionsAsync(member);
        return Permission.HasPermission(perms, permission);
    }

    /// <summary>
    /// When a channel's inheritance settings change, clear its caches.
    /// </summary>
    public async Task HandleChannelInheritanceChange(Channel channel)
    {
        if (channel.PlanetId is null)
            return;

        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(channel.PlanetId.Value);

        // Clear caches for any channels that inherit from this one
        var inheritorsList = hostedPlanet.GetInheritors(channel.Id);
        if (inheritorsList is not null)
        {
            foreach (var inheritorId in inheritorsList)
                hostedPlanet.PermissionCache.ClearCacheForChannel(inheritorId);
        }

        hostedPlanet.PermissionCache.ClearCacheForChannel(channel.Id);
        hostedPlanet.ClearInheritanceCache(channel.Id);
    }

    /// <summary>
    /// When a permissions node changes (for a given channel) clear all per–(role,channel) caches
    /// and the inverse mapping for that channel, including channels that inherit from it.
    /// </summary>
    public async Task HandleNodeChange(PermissionsNode node)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(node.PlanetId);
        var roleCombos = hostedPlanet.PermissionCache.GetCombosForRole(node.RoleId);
        if (roleCombos != null)
        {
            foreach (var roleKey in roleCombos)
                hostedPlanet.PermissionCache.ClearCacheForComboAndChannel(roleKey, node.TargetId);
        }

        hostedPlanet.PermissionCache.ClearChannelAccessRoleComboCache(node.TargetId);

        // Also clear caches for channels that inherit permissions from this channel,
        // since their effective permissions are derived from this node's target.
        var inheritors = hostedPlanet.GetInheritors(node.TargetId);
        if (inheritors is not null)
        {
            foreach (var inheritorId in inheritors)
            {
                hostedPlanet.PermissionCache.ClearCacheForChannel(inheritorId);
                hostedPlanet.PermissionCache.ClearChannelAccessRoleComboCache(inheritorId);
            }
        }
    }

    /// <summary>
    /// When a role changes, clear all caches for any role–combo that includes it and clear the inverse mapping cache.
    /// </summary>
    public async Task HandleRoleChange(PlanetRole role)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(role.PlanetId);
        var roleCombos = hostedPlanet.PermissionCache.GetCombosForRole(role.Id);
        if (roleCombos != null)
        {
            foreach (var roleKey in roleCombos)
                hostedPlanet.PermissionCache.ClearCacheForCombo(roleKey);
        }

        hostedPlanet.PermissionCache.ClearAllChannelAccessRoleComboCache();
    }
    
    /// <summary>
    /// When role order changes, nuke the entire permissions cache. Too much can change.
    /// </summary>
    public async Task HandleRoleOrderChange(long planetId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        hostedPlanet.PermissionCache.Clear();
    }

    /// <summary>
    /// Clears all channel-access and inheritance caches for a planet.
    /// This is intentionally broad and used after channel topology changes
    /// (create/delete/move/update) to avoid serving stale access snapshots.
    /// </summary>
    public async Task HandleChannelTopologyChange(long planetId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        hostedPlanet.PermissionCache.Clear();
        hostedPlanet.ClearAllInheritanceCaches();
    }

    public async ValueTask<bool> HasChannelAccessAsync(long memberId, long channelId)
    {
        var access = await GetChannelAccessAsync(memberId);
        return access != null && access.Contains(channelId);
    }

    public async ValueTask<uint> GetAuthorityAsync(PlanetMember member)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(member.PlanetId);
        if (hostedPlanet.Planet.OwnerId == member.UserId)
            return uint.MaxValue;

        var cached = hostedPlanet.PermissionCache.GetAuthority(member.RoleMembership);
        if (cached is not null)
            return cached.Value;
        
        // Get all the roles the member has
        PlanetRole? highestAuthorityRole = null;
        foreach (var roleIndex in member.RoleMembership.EnumerateRoleIndices())
        {
            var role = hostedPlanet.GetRoleByIndex(roleIndex);
            if (role is null)
                continue;
            
            // Remember: lower position = higher authority
            if (highestAuthorityRole is null || role.Position < highestAuthorityRole.Position)
                highestAuthorityRole = role;
        }

        var authority = highestAuthorityRole?.GetAuthority() ?? 0;
        hostedPlanet.PermissionCache.SetAuthority(member.RoleMembership, authority);
        return authority;
    }

    public async ValueTask<ModelListSnapshot<Channel, long>?> GetChannelAccessAsync(long memberId)
    {
        var member = await _db.PlanetMembers.FindAsync(memberId);
        if (member is null)
            return null;

        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(member.PlanetId);
        if (member.UserId == hostedPlanet.Planet.OwnerId)
            return hostedPlanet.Channels;

        var cached = hostedPlanet.PermissionCache.GetChannelAccess(member.RoleMembership);
        if (cached is not null)
        {
            // Self-heal stale snapshots instead of requiring a node restart.
            if (!IsChannelAccessSnapshotStale(cached, hostedPlanet.Channels))
                return cached;

            _logger.LogWarning(
                "Detected stale channel-access cache for member {MemberId} in planet {PlanetId}; rebuilding.",
                memberId,
                member.PlanetId
            );

            hostedPlanet.PermissionCache.ClearCacheForCombo(member.RoleMembership);
        }

        // If invalidations race with permission computation, retry once using the
        // latest cache generation to avoid returning stale snapshots.
        const int maxAttempts = 2;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var generation = hostedPlanet.PermissionCache.Generation;
            var access = await GenerateChannelAccessAsync(member, generation);

            if (hostedPlanet.PermissionCache.Generation == generation)
            {
                return access.Snapshot;
            }

            hostedPlanet.PermissionCache.EvictAccessCacheEntry(member.RoleMembership);
        }

        // Fallback: return the latest computation even if churn continues.
        var fallback = await GenerateChannelAccessAsync(member, hostedPlanet.PermissionCache.Generation);
        return fallback.Snapshot;
    }

    private static bool IsChannelAccessSnapshotStale(
        ModelListSnapshot<Channel, long> cachedAccess,
        ModelListSnapshot<Channel, long> allChannels)
    {
        // If the planet has channels but cached access is empty, this is almost always stale.
        if (allChannels.List.Count > 0 && cachedAccess.List.Count == 0)
            return true;

        // Every cached channel should still exist in the hosted planet channel list.
        foreach (var channel in cachedAccess.List)
        {
            if (!allChannels.Contains(channel.Id))
                return true;
        }

        // Default channel should always be visible to members.
        var defaultChannel = allChannels.List.FirstOrDefault(x => x.IsDefault);
        if (defaultChannel is not null && !cachedAccess.Contains(defaultChannel.Id))
            return true;

        return false;
    }

    private async Task<SortedServerModelList<Channel, long>> GenerateChannelAccessAsync(
        Valour.Database.PlanetMember member, long generation)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(member.PlanetId);

        var allChannels = hostedPlanet.Channels;
        var roles = RoleListPool.Get();
        bool isAdmin = false;
        foreach (var roleIndex in member.RoleMembership.EnumerateRoleIndices())
        {
            var role = hostedPlanet.GetRoleByIndex(roleIndex);
            if (role is null)
                continue;

            hostedPlanet.PermissionCache.AddKnownComboToRole(role.Id, member.RoleMembership);
            if (role.IsAdmin)
            {
                isAdmin = true;
                break;
            }

            roles.Add(role);
        }

        if (isAdmin)
        {
            var adminResult = hostedPlanet.PermissionCache.SetChannelAccess(member.RoleMembership, allChannels.List);
            RoleListPool.Return(roles);
            return adminResult;
        }

        // Sort roles by position ascending (lower position = higher authority = first)
        roles.Sort(ISortable.Comparer);
        var access = hostedPlanet.PermissionCache.GetEmptyAccessList();
        foreach (var channel in allChannels.List)
        {
            if (channel.IsDefault)
            {
                access.Add(channel);
                continue;
            }

            // Resolve permission inheritance: if the channel inherits permissions,
            // walk up the parent chain to find the effective target channel whose
            // permission nodes should be used.
            ISharedChannel effectiveChannel = channel;
            if (channel.InheritsPerms)
            {
                if (hostedPlanet.TryGetInheritanceTarget(channel.Id, out var targetId))
                {
                    if (targetId is not null)
                    {
                        var target = hostedPlanet.GetChannel(targetId.Value);
                        if (target is not null)
                            effectiveChannel = target;
                    }
                }
                else
                {
                    var walkChannel = (ISharedChannel)channel;
                    while (walkChannel.InheritsPerms && walkChannel.ParentId is not null)
                    {
                        var parent = hostedPlanet.GetChannel(walkChannel.ParentId.Value);
                        if (parent is null)
                            break;
                        walkChannel = parent;
                    }

                    // Only cache the inheritance mapping if no topology change occurred
                    // during this computation.  A racing HandleChannelTopologyChange clears
                    // the inheritance cache; writing after that clear would poison it with
                    // stale mappings that subsequent computations would trust.
                    if (hostedPlanet.PermissionCache.Generation == generation)
                        hostedPlanet.SetInheritanceTarget(channel.Id, walkChannel.Id);

                    effectiveChannel = walkChannel;
                }
            }

            long? perms = channel.ChannelType switch
            {
                ChannelTypeEnum.PlanetChat => await GenerateChannelPermissionsAsync(member.RoleMembership, roles, effectiveChannel,
                    hostedPlanet, ChannelTypeEnum.PlanetChat, generation),
                ChannelTypeEnum.PlanetCategory => await GenerateChannelPermissionsAsync(member.RoleMembership, roles,
                    effectiveChannel, hostedPlanet, ChannelTypeEnum.PlanetCategory, generation),
                ChannelTypeEnum.PlanetVoice => await GenerateChannelPermissionsAsync(member.RoleMembership, roles, effectiveChannel,
                    hostedPlanet, ChannelTypeEnum.PlanetVoice, generation),
                _ => throw new Exception("Invalid channel type!")
            };

            if (Permission.HasPermission(perms.Value, ChannelPermissions.View))
                access.Add(channel);
        }

        RoleListPool.Return(roles);
        
        SortedServerModelList<Channel, long> result;
        if (hostedPlanet.PermissionCache.Generation == generation)
        {
            result = hostedPlanet.PermissionCache.SetChannelAccess(member.RoleMembership, access);
        }
        else
        {
            // Avoid poisoning access cache with stale data if invalidated mid-computation.
            result = new SortedServerModelList<Channel, long>();
            result.Set(access);
        }
        
        PlanetPermissionsCache.AccessListPool.Return(access);
        return result;
    }

    public async ValueTask<bool> IsAdminAsync(ISharedPlanetMember member)
    {
        return await GetPlanetPermissionsAsync(member) == Permission.FULL_CONTROL;
    }

    private async ValueTask<long> GetPlanetPermissionsAsync(ISharedPlanetMember member)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(member.PlanetId);
        if (member.UserId == hostedPlanet.Planet.OwnerId)
            return Permission.FULL_CONTROL;
        
        var cached = hostedPlanet.PermissionCache.GetPlanetPermissions(member.RoleMembership);
        if (cached is not null)
            return cached.Value;

        long permissions = 0;
        foreach (var roleIndex in member.RoleMembership.EnumerateRoleIndices())
        {
            var role = hostedPlanet.GetRoleByIndex(roleIndex);
            if (role is null)
                continue;
            
            hostedPlanet.PermissionCache.AddKnownComboToRole(role.Id, member.RoleMembership);
            if (role.IsAdmin)
            {
                permissions = Permission.FULL_CONTROL;
                break;
            }

            permissions |= role.Permissions;
        }

        hostedPlanet.PermissionCache.SetPlanetPermissions(member.RoleMembership, permissions);
        return permissions;
    }

    public async ValueTask<bool> HasChannelPermissionAsync(ISharedPlanetMember member, ISharedChannel channel,
        ChannelPermission permission)
    {
        var perms = await GetChannelPermissionsAsync(member, channel, permission.TargetType);
        return Permission.HasPermission(perms, permission);
    }

    public async ValueTask<long> GetChannelPermissionsAsync(ISharedPlanetMember member, ISharedChannel channel,
        ChannelTypeEnum targetType)
    {
        var hosted = await _hostedPlanetService.GetRequiredAsync(member.PlanetId);
        if (member.UserId == hosted.Planet.OwnerId)
            return Permission.FULL_CONTROL;

        // Capture generation early so we can guard inheritance cache writes below.
        var generation = hosted.PermissionCache.Generation;

        var initialChannelId = channel.Id;
        if (channel.InheritsPerms)
        {
            // Check cached inheritance target
            if (hosted.TryGetInheritanceTarget(channel.Id, out var targetId))
            {
                if (targetId is not null)
                {
                    var targetChannel = hosted.GetChannel(targetId.Value);
                    if (targetChannel is not null)
                        channel = targetChannel;
                }
            }
            else
            {
                // Walk up the parent chain to find the non-inheriting ancestor
                while (channel.InheritsPerms && channel.ParentId is not null)
                {
                    var parent = hosted.GetChannel(channel.ParentId.Value);
                    if (parent is null)
                        break;
                    channel = parent;
                }

                // Only cache if no topology change occurred; see GenerateChannelAccessAsync.
                if (hosted.PermissionCache.Generation == generation)
                    hosted.SetInheritanceTarget(initialChannelId, channel.Id);
            }
        }

        var channelKey = PlanetPermissionUtils.GetRoleChannelComboKey(member.RoleMembership, channel.Id);
        // Use targetType for cache, not channel.ChannelType, because when a chat channel
        // inherits from a category, the effective channel is the category but we need to
        // cache chat permissions separately from category permissions.
        var cache = hosted.PermissionCache.GetChannelCache(targetType);
        var cachedPerms = cache.GetChannelPermission(channelKey);
        if (cachedPerms is not null)
            return cachedPerms.Value;

        var roles = RoleListPool.Get();

        foreach (var roleIndex in member.RoleMembership.EnumerateRoleIndices())
        {
            var role = hosted.GetRoleByIndex(roleIndex);
            if (role is null)
            {
                _logger.LogWarning("Role not found for role index {RoleIndex} in planet {PlanetId}", roleIndex, member.PlanetId);
                continue;
            }

            // Register this combo so that cache invalidation (HandleNodeChange/HandleRoleChange)
            // can find and clear per-channel entries created through this path.
            hosted.PermissionCache.AddKnownComboToRole(role.Id, member.RoleMembership);
            roles.Add(role);
        }

        // Roles need to be ordered by position descending (weakest to strongest)
        roles.Sort(ISortable.ComparerDescending);

        var computedPerms =
            await GenerateChannelPermissionsAsync(member.RoleMembership, roles, channel, hosted, targetType, generation);

        RoleListPool.Return(roles);

        return computedPerms;
    }

    private async ValueTask<long> GenerateChannelPermissionsAsync(
        PlanetRoleMembership roleMembership,
        List<PlanetRole> roles,
        ISharedChannel channel,
        HostedPlanet hostedPlanet,
        ChannelTypeEnum targetType,
        long expectedGeneration)
    {
        var channelKey = PlanetPermissionUtils.GetRoleChannelComboKey(roleMembership, channel.Id);
        // Use targetType for cache, not channel.ChannelType - the effective channel may be
        // a category but we're computing permissions for a specific channel type (chat/voice/category).
        var permCache = hostedPlanet.PermissionCache.GetChannelCache(targetType);

        var cachedPerms = permCache.GetChannelPermission(channelKey);
        if (cachedPerms != null)
            return cachedPerms.Value;

        if (roles.Count == 0)
        {
            if (hostedPlanet.PermissionCache.Generation == expectedGeneration)
                permCache.Set(roleMembership, channel.Id, 0);
            return 0;
        }

        if (roles.Any(x => x.IsAdmin))
        {
            if (hostedPlanet.PermissionCache.Generation == expectedGeneration)
                permCache.Set(roleMembership, channel.Id, Permission.FULL_CONTROL);
            return Permission.FULL_CONTROL;
        }

        var targetRoleIds = roles.Select(r => r.Id).ToArray();
        var permNodes = await _db.PermissionsNodes.AsNoTracking()
            .Where(x =>
                x.TargetType == targetType &&
                x.TargetId == channel.Id &&
                targetRoleIds.Contains(x.RoleId))
            .OrderByDescending(x => x.Role.Position)
            .ToListAsync();

        var permissions = PermissionCalculator.GetChannelPermissions(roles, targetType, permNodes);

        // Only cache if the permission data hasn't been invalidated during computation.
        // This prevents a long-running computation from writing back stale data after
        // a concurrent invalidation cleared the cache.
        if (hostedPlanet.PermissionCache.Generation == expectedGeneration)
            permCache.Set(roleMembership, channel.Id, permissions);

        return permissions;
    }
}
