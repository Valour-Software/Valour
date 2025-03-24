#nullable enable

using System.Collections.Concurrent;
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

    // Inheritance maps for channels.
    private static readonly ConcurrentDictionary<long, long?> _inheritanceMap = new();
    private static readonly ConcurrentDictionary<long, ConcurrentHashSet<long>> _inheritanceLists = new();

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
        
        for (int i = 0; i < combos.Length; i++)
        {
            var combo = combos[i];
            var roleIndices = combo.GetRoleIndices();
            var roles = new PlanetRole[roleIndices.Length];
            
            for (int j = 0; j < roleIndices.Length; j++)
            {
                var role = hostedPlanet.GetRoleByIndex(roleIndices[j]);
                if (role is null)
                {
                    _logger.LogWarning("Role not found for role index {RoleIndex} in planet {PlanetId}", roleIndices[j], planetId);
                    continue;
                }
                
                roles[j] = role;
            }

            roleArrs[i] = roles;
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

    /// <summary>
    /// When a channel’s inheritance settings change, clear its caches.
    /// </summary>
    public async Task HandleChannelInheritanceChange(Channel channel)
    {
        if (channel.PlanetId is null)
            return;

        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(channel.PlanetId.Value);
        if (_inheritanceLists.TryGetValue(channel.Id, out var inheritorsList))
        {
            foreach (var inheritorId in inheritorsList)
                hostedPlanet.PermissionCache.ClearCacheForChannel(inheritorId);
        }

        hostedPlanet.PermissionCache.ClearCacheForChannel(channel.Id);
    }

    /// <summary>
    /// When a permissions node changes (for a given channel) clear all per–(role,channel) caches
    /// and the inverse mapping for that channel.
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
        PlanetRole? highestRole = null;
        foreach (var roleIndex in member.RoleMembership.EnumerateRoleIndices())
        {
            var role = hostedPlanet.GetRoleByIndex(roleIndex);
            if (role is null)
                continue;
            if (highestRole is null || role.Position > highestRole.Position)
                highestRole = role;
        }

        var authority = highestRole?.GetAuthority() ?? 0;
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
            return cached;

        var access = await GenerateChannelAccessAsync(member);
        return access.Snapshot;
    }

    private async Task<SortedServerModelList<Channel, long>> GenerateChannelAccessAsync(
        Valour.Database.PlanetMember member)
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

        roles.Sort(ISortable.Comparer);
        var access = hostedPlanet.PermissionCache.GetEmptyAccessList();
        foreach (var channel in allChannels.List)
        {
            if (channel.IsDefault)
            {
                access.Add(channel);
                continue;
            }

            long? perms = channel.ChannelType switch
            {
                ChannelTypeEnum.PlanetChat => await GenerateChannelPermissionsAsync(member.RoleMembership, roles, channel,
                    hostedPlanet, ChannelTypeEnum.PlanetChat),
                ChannelTypeEnum.PlanetCategory => await GenerateChannelPermissionsAsync(member.RoleMembership, roles,
                    channel, hostedPlanet, ChannelTypeEnum.PlanetCategory),
                ChannelTypeEnum.PlanetVoice => await GenerateChannelPermissionsAsync(member.RoleMembership, roles, channel,
                    hostedPlanet, ChannelTypeEnum.PlanetVoice),
                _ => throw new Exception("Invalid channel type!")
            };

            if (Permission.HasPermission(perms.Value, ChannelPermissions.View))
                access.Add(channel);
        }
        
        RoleListPool.Return(roles);
        var result = hostedPlanet.PermissionCache.SetChannelAccess(member.RoleMembership, access);
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

        var initialChannelId = channel.Id;
        if (channel.InheritsPerms)
        {
            if (_inheritanceMap.TryGetValue(channel.Id, out var targetId))
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
                while (channel.InheritsPerms && channel.ParentId is not null)
                {
                    var parent = hosted.GetChannel(channel.ParentId.Value);
                    if (parent is null)
                        break;
                    channel = parent;
                }

                _inheritanceMap[initialChannelId] = channel.Id;
                if (!_inheritanceLists.TryGetValue(channel.Id, out var inheritorsList))
                {
                    inheritorsList = new ConcurrentHashSet<long>() { initialChannelId };
                    _inheritanceLists[channel.Id] = inheritorsList;
                }
                else
                {
                    inheritorsList.Add(initialChannelId);
                }
            }
        }

        var channelKey = PlanetPermissionUtils.GetRoleChannelComboKey(member.RoleMembership, channel.Id);
        var cache = hosted.PermissionCache.GetChannelCache(channel.ChannelType);
        var cachedPerms = cache.GetChannelPermission(channelKey);
        if (cachedPerms is not null)
            return cachedPerms.Value;

        var roleIndices = member.RoleMembership.GetRoleIndices();
        var roles = RoleListPool.Get();

        for (int i = 0; i < roleIndices.Length; i++)
        {
            var roleIndex = roleIndices[i];
            var role = hosted.GetRoleByIndex(roleIndex);
            if (role is null)
                _logger.LogWarning("Role not found for role index {RoleIndex} in planet {PlanetId}", roleIndex, member.PlanetId);
            
            roles.Add(role!);
        }
        
        var computedPerms =
            await GenerateChannelPermissionsAsync(member.RoleMembership, roles, channel, hosted, targetType);
        
        RoleListPool.Return(roles);
        
        cache.Set(member.RoleMembership, channel.Id, computedPerms);
        
        return computedPerms;
    }

    private async ValueTask<long> GenerateChannelPermissionsAsync(
        PlanetRoleMembership roleMembership,
        List<PlanetRole> roles,
        ISharedChannel channel,
        HostedPlanet hostedPlanet,
        ChannelTypeEnum targetType)
    {
        var channelKey = PlanetPermissionUtils.GetRoleChannelComboKey(roleMembership, channel.Id);
        var permCache = hostedPlanet.PermissionCache.GetChannelCache(channel.ChannelType);

        var cachedPerms = permCache.GetChannelPermission(channelKey);
        if (cachedPerms != null)
            return cachedPerms.Value;

        if (roles.Any(x => x.IsAdmin))
        {
            permCache.Set(roleMembership, channel.Id, Permission.FULL_CONTROL);
            return Permission.FULL_CONTROL;
        }

        var targetRoleIds = roles.Select(r => r.Id).ToList();
        var permNodes = await _db.PermissionsNodes.AsNoTracking()
            .Where(x =>
                x.TargetType == targetType &&
                x.TargetId == channel.Id &&
                targetRoleIds.Contains(x.RoleId))
            .OrderByDescending(x => x.Role.Position)
            .ToListAsync();

        long permissions = targetType switch
        {
            ChannelTypeEnum.PlanetChat => roles.Last().ChatPermissions,
            ChannelTypeEnum.PlanetCategory => roles.Last().CategoryPermissions,
            ChannelTypeEnum.PlanetVoice => roles.Last().VoicePermissions,
            _ => throw new Exception("Invalid channel type!")
        };

        foreach (var node in permNodes)
        {
            permissions &= ~node.Mask;
            permissions |= (node.Code & node.Mask);
        }

        permCache.Set(roleMembership, channel.Id, permissions);
        return permissions;
    }
}