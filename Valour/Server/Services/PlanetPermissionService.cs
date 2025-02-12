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
    private const long Seed = unchecked((long)0xcbf29ce484222325); // FNV1a seed
    private const long MagicNumber = unchecked((long)0x9e3779b97f4a7c15);

    public static readonly ObjectPool<List<PlanetRole>> RoleListPool =
        new DefaultObjectPool<List<PlanetRole>>(new ListPooledObjectPolicy<PlanetRole>());

    // Inheritance maps for channels.
    private static readonly ConcurrentDictionary<long, long?> _inheritanceMap = new();
    private static readonly ConcurrentDictionary<long, ConcurrentHashSet<long>> _inheritanceLists = new();

    private readonly ValourDb _db;
    private readonly HostedPlanetService _hostedPlanetService;
    private readonly ILogger<PlanetPermissionService> _logger;

    public PlanetPermissionService(ValourDb db, HostedPlanetService hostedPlanetService,
        ILogger<PlanetPermissionService> logger)
    {
        _db = db;
        _hostedPlanetService = hostedPlanetService;
        _logger = logger;
    }

    /// <summary>
    /// Returns all the distinct role combinations that exist for an existing role on the planet.
    /// This only returns combinations that are in use.
    /// </summary>
    public async Task<RoleComboInfo[]> GetPlanetRoleCombosForRole(long roleId)
    {
        var roleCombos = await _db.PlanetRoleMembers
            .Where(x => x.RoleId == roleId)
            .Select(x => x.Member)
            .Include(m => m.RoleMembership)
            .ThenInclude(rm => rm.Role)
            .GroupBy(x => x.RoleHashKey)
            .Select(g => g.First())
            .Select(x => new RoleComboInfo()
            {
                RoleHashKey = x.RoleHashKey,
                Roles = x.RoleMembership.Select(y => y.Role.Id).Order().ToArray()
            })
            .ToArrayAsync();

        return roleCombos;
    }

    /// <summary>
    /// Returns all the distinct role hash keys that exist for an existing role on the planet.
    /// This only returns combinations that are in use.
    /// </summary>
    public async Task<long[]> GetPlanetRoleComboKeysForRole(long roleId)
    {
        var roleCombos = await _db.PlanetRoleMembers
            .Where(x => x.RoleId == roleId)
            .Select(x => x.Member)
            .Include(m => m.RoleMembership)
            .ThenInclude(rm => rm.Role)
            .GroupBy(x => x.RoleHashKey)
            .Select(g => g.First())
            .Select(x => x.RoleHashKey)
            .ToArrayAsync();

        return roleCombos;
    }

    /// <summary>
    /// Returns all the distinct role combinations that exist for an existing role on the planet.
    /// THIS VERSION REMOVES THE ROLE ITSELF, GIVING THE NEW ROLE LISTS
    /// This only returns combinations that are in use.
    /// </summary>
    public async Task<RoleComboInfo[]> GetPlanetRoleCombosForRolePostDeletion(long roleId)
    {
        return await _db.PlanetRoleMembers
            .Where(rm => rm.RoleId == roleId)
            .GroupBy(rm => rm.Member.RoleHashKey)
            .Select(g => new RoleComboInfo
            {
                RoleHashKey = g.Key,
                Roles = g.SelectMany(rm => rm.Member.RoleMembership)
                    .Where(rm => rm.RoleId != roleId)
                    .Select(rm => rm.Role.Id)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToArray()
            })
            .ToArrayAsync();
    }

    /// <summary>
    /// Returns all the distinct role combination keys that exist on the planet.
    /// This only returns combinations that are in use.
    /// </summary>
    public async Task<long[]> GetPlanetRoleComboKeysAsync(long planetId)
    {
        var distinctRoleKeys = await _db.PlanetMembers
            .Where(x => x.PlanetId == planetId)
            .Select(x => x.RoleHashKey) // now safe to access .Value
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
            .Select(g => g.First())
            .Select(x => x.RoleMembership.Select(y => y.Role).ToArray())
            .ToArrayAsync();

        return roleCombos;
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

    public async Task UpdateMemberRoleHashAsync(long memberId, bool saveChanges = true)
    {
        var member = await _db.PlanetMembers.FindAsync(memberId);
        if (member is null)
            return;

        await UpdateMemberRoleHashAsync(member, saveChanges);
    }

    public async Task<long> GetOrUpdateRoleHashKeyAsync(ISharedPlanetMember member)
    {
        var roleKey = await CalculateRoleHashKeyAsync(member.PlanetId, member.Id);
        member.RoleHashKey = roleKey;
        await _db.PlanetMembers.Where(x => x.Id == member.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.RoleHashKey, roleKey));
        return roleKey;
    }

    public async Task<long> CalculateRoleHashKeyAsync(long planetId, long memberId)
    {
        var roleIds = await _db.PlanetRoleMembers
            .Where(x => x.MemberId == memberId)
            .Select(x => x.RoleId)
            .OrderBy(x => x)
            .ToArrayAsync();
        
        var key = GenerateRoleComboKey(roleIds);

        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        hostedPlanet.SetRoleCombo(key, roleIds);
        
        return key;
    }

    public async Task UpdateMemberRoleHashAsync(Valour.Database.PlanetMember member, bool saveChanges = true)
    {
        var roleKey = await CalculateRoleHashKeyAsync(member.PlanetId, member.Id);
        member.RoleHashKey = roleKey;
        
        if (saveChanges)
            await _db.SaveChangesAsync();
    }

    public async Task BulkUpdateMemberRoleHashesAsync(int saveInterval = 50)
    {
        var members = await _db.PlanetMembers.ToListAsync();
        int count = 0;
        foreach (var member in members)
        {
            if (member.RoleHashKey == 0)
                await UpdateMemberRoleHashAsync(member.Id, false);
            if (count % saveInterval == 0)
            {
                await _db.SaveChangesAsync();
                _logger.LogInformation("Bulk role hash update: {Count} members updated", count);
            }

            count++;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Bulk role hash update: {Count} members updated - Finished", count);
    }

    /// <summary>
    /// Returns all distinct role–hash keys (role combos) that have “view” permission for the given channel.
    /// </summary>
    public async Task<List<long>> GetRoleHashesThatCanAccessChannelAsync(long planetId, long channelId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        var channel = hostedPlanet.GetChannel(channelId);
        if (channel is null)
            return new List<long>();

        // Use the lazy cache to avoid duplicate computations.
        var lazyResult = hostedPlanet.PermissionCache.ChannelAccessRoleComboCache.GetOrAdd(
            channelId,
            _ => new Lazy<Task<List<long>>>(() => ComputeAccessibleRoleCombosAsync(hostedPlanet, channel)));

        try
        {
            var result = await lazyResult.Value;
            return result;
        }
        catch
        {
            // In case of failure, remove the lazy entry.
            hostedPlanet.PermissionCache.ChannelAccessRoleComboCache.TryRemove(channelId, out _);
            throw;
        }
    }

    /// <summary>
    /// Helper method that computes, by iterating over all known role–hash keys,
    /// which ones grant view permission on the given channel.
    /// </summary>
    private async Task<List<long>> ComputeAccessibleRoleCombosAsync(HostedPlanet hostedPlanet, Channel channel)
    {
        // Get all distinct role combos in use.
        var allRoleComboKeys = await GetPlanetRoleComboKeysAsync(hostedPlanet.Planet.Id);
        var channelCache = hostedPlanet.PermissionCache.GetChannelCache(channel.ChannelType);
        var accessibleRoleCombos = new List<long>();

        foreach (var roleKey in allRoleComboKeys)
        {
            var combinedKey = GetRoleChannelComboKey(roleKey, channel.Id);
            long? perms = channelCache.GetChannelPermission(combinedKey);
            if (perms == null)
            {
                var member = await _db.PlanetMembers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.PlanetId == hostedPlanet.Planet.Id && m.RoleHashKey == roleKey);
                if (member != null)
                    perms = await GetChannelPermissionsAsync(member, channel, channel.ChannelType);
            }

            if (perms != null && Permission.HasPermission(perms.Value, ChannelPermissions.View))
                accessibleRoleCombos.Add(roleKey);
        }

        return accessibleRoleCombos;
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

        var roleHashKey = await GetOrUpdateRoleHashKeyAsync(member);
        var cached = hostedPlanet.PermissionCache.GetAuthority(roleHashKey);
        if (cached is not null)
            return cached.Value;

        var rolePos = await _db.PlanetRoleMembers
            .AsNoTracking()
            .Where(x => x.MemberId == member.Id)
            .Include(x => x.Role)
            .OrderBy(x => x.Role.Position)
            .Select(x => x.Role.Position)
            .FirstAsync();

        var authority = uint.MaxValue - rolePos;
        hostedPlanet.PermissionCache.SetAuthority(roleHashKey, authority);
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

        var cached = hostedPlanet.PermissionCache.GetChannelAccess(member.RoleHashKey);
        if (cached is not null)
            return cached;

        var access = await GenerateChannelAccessAsync(member);
        return access.Snapshot;
    }

    private async Task<SortedServerModelList<Channel, long>> GenerateChannelAccessAsync(
        Valour.Database.PlanetMember member)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(member.PlanetId);
        var roleMembership = await _db.PlanetRoleMembers
            .Where(x => x.MemberId == member.Id)
            .Select(x => x.RoleId)
            .ToListAsync();

        var allChannels = hostedPlanet.Channels;
        var roles = RoleListPool.Get();
        bool isAdmin = false;
        foreach (var roleId in roleMembership)
        {
            var role = hostedPlanet.GetRole(roleId);
            if (role is null)
                continue;
            hostedPlanet.PermissionCache.AddKnownComboToRole(roleId, member.RoleHashKey);
            if (role.IsAdmin)
            {
                isAdmin = true;
                break;
            }

            roles.Add(role);
        }

        if (isAdmin)
        {
            var adminResult = hostedPlanet.PermissionCache.SetChannelAccess(member.RoleHashKey, allChannels.List);
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
                ChannelTypeEnum.PlanetChat => await GenerateChannelPermissionsAsync(member.RoleHashKey, roles, channel,
                    hostedPlanet, ChannelTypeEnum.PlanetChat),
                ChannelTypeEnum.PlanetCategory => await GenerateChannelPermissionsAsync(member.RoleHashKey, roles,
                    channel, hostedPlanet, ChannelTypeEnum.PlanetCategory),
                ChannelTypeEnum.PlanetVoice => await GenerateChannelPermissionsAsync(member.RoleHashKey, roles, channel,
                    hostedPlanet, ChannelTypeEnum.PlanetVoice),
                _ => throw new Exception("Invalid channel type!")
            };

            if (Permission.HasPermission(perms.Value, ChannelPermissions.View))
                access.Add(channel);
        }
        
        RoleListPool.Return(roles);
        var result = hostedPlanet.PermissionCache.SetChannelAccess(member.RoleHashKey, access);
        return result;
    }

    private async ValueTask<long> GetPlanetPermissionsAsync(Valour.Database.PlanetMember member)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(member.PlanetId);
        var cached = hostedPlanet.PermissionCache.GetPlanetPermissions(member.RoleHashKey);
        if (cached is not null)
            return cached.Value;

        var roleMembership = await _db.PlanetRoleMembers
            .Where(x => x.MemberId == member.Id)
            .Select(x => x.RoleId)
            .ToListAsync();

        long permissions = 0;
        foreach (var roleId in roleMembership)
        {
            var role = hostedPlanet.GetRole(roleId);
            if (role is null)
                continue;
            hostedPlanet.PermissionCache.AddKnownComboToRole(roleId, member.RoleHashKey);
            if (role.IsAdmin)
            {
                permissions = Permission.FULL_CONTROL;
                break;
            }

            permissions |= role.Permissions;
        }

        hostedPlanet.PermissionCache.SetPlanetPermissions(member.RoleHashKey, permissions);
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

        var channelKey = GetRoleChannelComboKey(member.RoleHashKey, channel.Id);
        var cache = hosted.PermissionCache.GetChannelCache(channel.ChannelType);
        var cachedPerms = cache.GetChannelPermission(channelKey);
        if (cachedPerms is not null)
            return cachedPerms.Value;

        var roleMembership = await _db.PlanetRoleMembers
            .Where(x => x.MemberId == member.Id)
            .Select(x => x.RoleId)
            .ToListAsync();

        var roles = RoleListPool.Get();
        foreach (var roleId in roleMembership)
        {
            var role = hosted.GetRole(roleId);
            if (role is null)
                throw new Exception("Role not found in hosted planet roles!");
            roles.Add(role);
        }

        var computedPerms =
            await GenerateChannelPermissionsAsync(member.RoleHashKey, roles, channel, hosted, targetType);
        RoleListPool.Return(roles);
        cache.Set(member.RoleHashKey, channel.Id, computedPerms);
        return computedPerms;
    }

    private async ValueTask<long> GenerateChannelPermissionsAsync(
        long roleKey,
        List<PlanetRole> roles,
        ISharedChannel channel,
        HostedPlanet hostedPlanet,
        ChannelTypeEnum targetType)
    {
        var channelKey = GetRoleChannelComboKey(roleKey, channel.Id);
        var permCache = hostedPlanet.PermissionCache.GetChannelCache(channel.ChannelType);

        var cachedPerms = permCache.GetChannelPermission(channelKey);
        if (cachedPerms != null)
            return cachedPerms.Value;

        if (roles.Any(x => x.IsAdmin))
        {
            permCache.Set(roleKey, channel.Id, Permission.FULL_CONTROL);
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

        permCache.Set(roleKey, channel.Id, permissions);
        return permissions;
    }

    private static long MixHash(long currentHash, long roleId)
    {
        return currentHash ^ ((roleId + MagicNumber) + (currentHash << 6) + (currentHash >> 2));
    }

    public static long GetRoleChannelComboKey(long rolesKey, long channelId)
    {
        var hash = MixHash(Seed, channelId);
        hash = MixHash(rolesKey, hash);
        return hash;
    }

    public static long GenerateRoleComboKey(IEnumerable<PlanetRole> roles)
    {
        long hash = Seed;
        foreach (var role in roles.OrderBy(x => x.Id))
            hash = MixHash(hash, role.Id);
        return hash;
    }

    public static long GenerateRoleComboKey(long[] sortedRoleIds)
    {
        long hash = Seed;
        foreach (var roleId in sortedRoleIds)
            hash = MixHash(hash, roleId);
        return hash;
    }
}