using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore.Storage;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Queries;

namespace Valour.Server.Services;

public class PlanetMemberService
{
    public static readonly TimeSpan OneDayConnectionWindow = TimeSpan.FromDays(1);

    private readonly ValourDb _db;
    private readonly CoreHubService _coreHub;
    private readonly TokenService _tokenService;
    private readonly PlanetPermissionService _permissionService;
    private readonly HostedPlanetService _hostedPlanetService;
    private readonly AutomodService _automodService;
    private readonly UserCacheService _userCache;
    private readonly ILogger<PlanetMemberService> _logger;
    
    private static readonly ConcurrentDictionary<(long, long), long> MemberIdLookup = new();

    /// <summary>
    /// A cached current member. It may not match the planet being requested (so check!) but it's
    /// highly likely to be the same, so it makes sense to cache it per request.
    /// </summary>
    private PlanetMember _currentMember;
    
    public PlanetMemberService(
        ValourDb db,
        CoreHubService coreHub,
        TokenService tokenService,
        ILogger<PlanetMemberService> logger,
        PlanetPermissionService permissionService,
        HostedPlanetService hostedPlanetService,
        AutomodService automodService,
        UserCacheService userCache)
    {
        _db = db;
        _coreHub = coreHub;
        _tokenService = tokenService;
        _logger = logger;
        _permissionService = permissionService;
        _hostedPlanetService = hostedPlanetService;
        _automodService = automodService;
        _userCache = userCache;
    }

    /// <summary>
    /// Resolves a user model for composition into a member, preferring the node-global user cache
    /// and falling back to the database (warming the cache) on a miss.
    /// </summary>
    private async ValueTask<User> ResolveUserAsync(long userId)
    {
        if (_userCache.TryGet(userId, out var cached))
            return cached;

        var user = (await _db.Users.FindAsync(userId)).ToModel();
        if (user is not null)
            _userCache.Set(user);

        return user;
    }

    /// <summary>
    /// Maps a database member to the public service model, ensuring the User field is populated
    /// from the node-global user cache when the EF entity was loaded without Include(x => x.User).
    /// </summary>
    private async ValueTask<PlanetMember> ToFullModelAsync(Valour.Database.PlanetMember member)
    {
        if (member is null)
            return null;

        var model = member.ToModel();
        model.User ??= await ResolveUserAsync(member.UserId);
        return model;
    }

    /// <summary>
    /// Returns the PlanetMember for the given id
    /// </summary>
    public async Task<PlanetMember> GetAsync(long id)
    {
        // Existence/role state stays database-authoritative (FirstOrDefault applies the
        // soft-delete query filter); only the user is composed from the cache to drop the join.
        var member = await _db.PlanetMembers
            .FirstOrDefaultAsync(x => x.Id == id);

        if (member is null)
            return null;

        return await ToFullModelAsync(member);
    }

    /// <summary>
    /// Returns the current user's PlanetMember for the given planet id
    /// </summary>
    public async Task<PlanetMember> GetCurrentAsync(long planetId)
    {
        if (_currentMember is not null && _currentMember.PlanetId == planetId)
            return _currentMember;
        
        var token = await _tokenService.GetCurrentTokenAsync();
        if (token is null)
            return null;

        var member =  await GetByUserAsync(token.UserId, planetId);
        
        _currentMember = member;
        
        return member;
    }

    /// <summary>
    /// Returns if the PlanetMember with the given id exists
    /// </summary>
    public async Task<bool> ExistsAsync(long id) =>
        await _db.PlanetMembers.AnyAsync(x => x.Id == id);
    
    /// <summary>
    /// Returns if the PlanetMember with the given user and planet ids exists
    /// </summary>
    public async Task<bool> ExistsAsync(long userId, long planetId) =>
        await _db.PlanetMembers.AnyAsync(x => x.UserId == userId && x.PlanetId == planetId);

    /// <summary>
    /// Returns if the member exists for the current user context and planet id
    /// </summary>
    public async Task<bool> CurrentExistsAsync(long planetId)
    {
        var token = await _tokenService.GetCurrentTokenAsync();
        if (token is null)
            return false;
        
        return await _db.PlanetMembers.AnyAsync(x => x.UserId == token.UserId && x.PlanetId == planetId);
    }

    /// <summary>
    /// Returns the PlanetMember for a given user id and planet id
    /// </summary>
    public async Task<PlanetMember> GetByUserAsync(long userId, long planetId)
    {
        // Fast path: if the planet is hosted on this node its member set is fully loaded and
        // authoritative, so resolve membership from memory and compose the user from the cache.
        var hosted = _hostedPlanetService.GetCached(planetId);
        if (hosted is not null && hosted.MembersLoaded)
        {
            if (hosted.TryGetMemberByUser(userId, out var cachedMember))
            {
                var user = await ResolveUserAsync(userId);
                return cachedMember.CopyWithUser(user);
            }

            // Cache miss: confirm against the database in case the member was added on a node that
            // doesn't host this planet (e.g. onboarding). Soft-deleted members are excluded by the
            // query filter, so a kicked member still resolves to null. Heal the cache on a hit.
            var confirmed = await _db.PlanetMembers
                .FirstOrDefaultAsync(x => x.PlanetId == planetId && x.UserId == userId);
            if (confirmed is null)
                return null;

            var confirmedModel = await ToFullModelAsync(confirmed);
            hosted.UpsertMember(confirmedModel);
            return confirmedModel;
        }

        // Fallback: planet not hosted here. Keep membership database-authoritative, composing the
        // user from the cache to avoid the user join.
        if (MemberIdLookup.TryGetValue((userId, planetId), out var memberId))
        {
            var member = await _db.PlanetMembers
                .FirstOrDefaultAsync(x => x.Id == memberId);

            if (member is not null)
            {
                return await ToFullModelAsync(member);
            }

            // Self-heal stale cache entries so future lookups can recover.
            MemberIdLookup.TryRemove((userId, planetId), out _);
        }

        var byUser = await _db.PlanetMembers
            .FirstOrDefaultAsync(x => x.PlanetId == planetId && x.UserId == userId);

        if (byUser is null)
            return null;

        MemberIdLookup[(userId, planetId)] = byUser.Id;

        return await ToFullModelAsync(byUser);
    }


    /// <summary>
    /// Returns the roles for the given member id
    /// </summary>
    public async Task<List<PlanetRole>> GetRolesAsync(ISharedPlanetMember member)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(member.PlanetId);
        var localIds = member.RoleMembership.GetRoleIndices();

        var roles = new List<PlanetRole>(localIds.Length);
        
        for (int i = 0; i < localIds.Length; i++)
        {
            var localId = localIds[i];
            var role = hostedPlanet.GetRoleByIndex(localId);
            if (role is not null)
                roles.Add(role);
        }

        roles.Sort(ISortable.Comparer);
            
        return roles;
    }

    /// <summary>
    /// Returns if the given member is an admin
    /// </summary>
    public async Task<bool> IsAdminAsync(long memberId)
    {
        var member = await _db.PlanetMembers.FindAsync(memberId);
        if (member is null)
            return false;

        return await _permissionService.IsAdminAsync(member);
    }
    
    /// <summary>
    /// Returns the authority of a planet member
    /// </summary>
    public async Task<uint> GetAuthorityAsync(PlanetMember member)
    {
        return await _permissionService.GetAuthorityAsync(member);
    }
    
    public async ValueTask<bool> HasRoleAsync(ISharedPlanetMember member, long roleId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(member.PlanetId);
        var role = hostedPlanet.GetRoleById(roleId);
        if (role is null)
            return false;
        
        var index = role.FlagBitIndex;
        return member.RoleMembership.HasRole(index);
    }
    
    /// <summary>
    /// Returns if a member has the given permission
    /// </summary>
    public async ValueTask<bool> HasPermissionAsync(long memberId, PlanetPermission permission)
    {
        return await _permissionService.HasPlanetPermissionAsync(memberId, permission);
    }
    
    /// <summary>
    /// Returns if a member has the given permission
    /// </summary>
    public async ValueTask<bool> HasPermissionAsync(PlanetMember member, PlanetPermission permission)
    {
        return await _permissionService.HasPlanetPermissionAsync(member.Id, permission);
    }

    public async Task<QueryResponse<PlanetMember>> QueryPlanetMembersAsync(
        long planetId,
        QueryRequest queryRequest)
    {
        var take = queryRequest.Take;
        if (take > 50)
            take = 50;
        
        var skip = queryRequest.Skip;
        
        var search = queryRequest.Options?.Filters?.GetValueOrDefault("search");

        var query = _db.PlanetMembers
            .AsNoTracking()
            .Include(x => x.User)
            .Where(x => x.PlanetId == planetId && !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var lowered = search.ToLower();
            query = query.Where(x =>
                (!string.IsNullOrEmpty(x.Nickname) &&
                 EF.Functions.ILike(x.Nickname.ToLower(), $"%{lowered}%")) ||
                 EF.Functions.ILike((x.User.Name.ToLower() + "#" + x.User.Tag), $"%{lowered}%") ||
                 EF.Functions.ILike(x.User.Name.ToLower(), $"%{lowered}%"));
        }

        var sortDesc = queryRequest.Options?.Sort?.Descending ?? false;
        query = queryRequest.Options?.Sort?.Field switch
        {
            "name" => sortDesc
                ? query.OrderByDescending(x => x.User.Name)
                : query.OrderBy(x => x.User.Name),
            _ => query.OrderBy(x => x.Id)
        };

        var total = await query.CountAsync();

        var items = await query
            .Skip(skip)
            .Take(take)
            .Select(x => x.ToModel())
            .ToListAsync();

        return new QueryResponse<PlanetMember>
        {
            Items = items,
            TotalCount = total
        };
    }

    /// <summary>
    /// Returns if the member has the given channel permission
    /// </summary>
    public async ValueTask<bool> HasPermissionAsync(PlanetMember member, Channel target, ChannelPermission permission)
    {
        if (target is null)
            return false;
        
        // If not planet channel, this doesn't apply
        if (!ISharedChannel.PlanetChannelTypes.Contains(target.ChannelType))
        {
            return true;
        }

        if (member is null)
            return false;

        return await _permissionService.HasChannelPermissionAsync(member, target, permission);
    }
    
    /// <summary>
    /// Adds the given user to the given planet as a member
    /// </summary>
    public async Task<TaskResult<PlanetMember>> AddMemberAsync(long planetId, long userId, bool doTransaction = true)
    {
        var planet = await _db.Planets.FindAsync(planetId);
        if (planet is null)
            return new TaskResult<PlanetMember>(false, "Planet not found.");
        if (planet.LockedForMigration)
            return new TaskResult<PlanetMember>(false, MigrationLock.Message);

        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return new TaskResult<PlanetMember>(false, "User not found.");

        if (planet.Nsfw)
        {
            var userPrivateInfo = await _db.PrivateInfos.FirstOrDefaultAsync(x => x.UserId == user.Id);
            if (userPrivateInfo is null)
                return new TaskResult<PlanetMember>(false, "An unexpected error occured.");

            // Check if the user is 18+
            if (userPrivateInfo.BirthDate.HasValue && userPrivateInfo.BirthDate.Value.AddYears(18) > DateTime.UtcNow)
            {
                return new TaskResult<PlanetMember>(false, "You must be 18+ to join this planet.");
            }
        }

        if (await _db.PlanetBans.AnyAsync(x => x.TargetId == user.Id && x.PlanetId == planet.Id &&
                                               (x.TimeExpires == null || x.TimeExpires > DateTime.UtcNow)))
        {
            return new TaskResult<PlanetMember>(false, "You are banned from this planet.");
        }

        // New member
        Valour.Database.PlanetMember member;
        
        // See if there is an old member
        var oldMember = await _db.PlanetMembers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.UserId == user.Id && x.PlanetId == planet.Id);

        // If there is an old member, we can just restore it
        bool rejoin = false;

        // Already a member
        if (oldMember is not null)
        {
            // If the member already exists and is not deleted, do nothing
            if (!oldMember.IsDeleted)
            {
                return new TaskResult<PlanetMember>(false, "Already a member.", null);
            }
            // Set old member to be restored
            else
            {
                member = oldMember;
                rejoin = true;
            }
        }
        else 
        {
            member = new Valour.Database.PlanetMember()
            {
                Id = IdManager.Generate(),
                PlanetId = planet.Id,
                UserId = user.Id,
                User = user,
                TimeLastConnected = DateTime.UtcNow,
                RoleMembership = new PlanetRoleMembership(0x01) // First bit (position 0) is the default role
            };
        }
        
        IDbContextTransaction trans = null;

        if (doTransaction)
            trans = await _db.Database.BeginTransactionAsync();

        try
        {
            if (rejoin)
            {
                member.IsDeleted = false;
                member.TimeLastConnected = DateTime.UtcNow;
                
                // Reset roles
                member.RoleMembership = new PlanetRoleMembership(0x01);
                
                _db.PlanetMembers.Update(member);
            }
            else
            {
                await _db.PlanetMembers.AddAsync(member);
            }
            
            // Restore old eco accounts
            var accounts = await _db.EcoAccounts.IgnoreQueryFilters()
                .Where(x => x.PlanetId == planet.Id && x.UserId == user.Id)
                .ToListAsync();

            foreach (var account in accounts)
            {
                account.PlanetMemberId = member.Id;
            }

            await _db.SaveChangesAsync();
            
        }
        catch (Exception e)
        {
            if (trans is not null)
                await trans.RollbackAsync();
            return new TaskResult<PlanetMember>(false, e.Message);
        }

        if (trans is not null)
            await trans.CommitAsync();

        var model = await ToFullModelAsync(member);

        // Keep the hosting node's caches in sync: register the (re)joined member and warm the
        // joining user so member reads don't immediately fall back to the database.
        _hostedPlanetService.GetCached(planetId)?.UpsertMember(model);
        _userCache.Set(user.ToModel());

        _coreHub.NotifyPlanetItemChange(model);

        await _automodService.HandleMemberJoinAsync(model);

        Console.WriteLine($"User {user.Name} ({user.Id}) has joined {planet.Name} ({planet.Id})");

        return new TaskResult<PlanetMember>(true, "Success", model);
    }

    /// <summary>
    /// Updates the given member in the database
    /// </summary>
    /// <returns></returns>
    public async Task<TaskResult<PlanetMember>> UpdateAsync(PlanetMember member)
    {
        var old = await _db.PlanetMembers
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == member.Id);

        if (old is null)
            return new TaskResult<PlanetMember>(false, "Member not found.");
        
        if (old.PlanetId != member.PlanetId)
            return new TaskResult<PlanetMember>(false, "Cannot change planet of member.");
        
        if (old.UserId != member.UserId)
            return new TaskResult<PlanetMember>(false, "Cannot change user of member.");

        var migrationGuard = await MigrationLock.GuardAsync(_db, old.PlanetId);
        if (!migrationGuard.Success)
            return new TaskResult<PlanetMember>(false, migrationGuard.Message);

        member.Nickname ??= string.Empty;
        var nameValid = ISharedPlanetMember.ValidateName(member);
        if (!nameValid.Success)
            return new TaskResult<PlanetMember>(false, nameValid.Message);

        try
        {
            // Self-updates should only modify nickname; role/avatar updates are managed by dedicated flows.
            old.Nickname = member.Nickname;
            _db.PlanetMembers.Update(old);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to update planet member {MemberId}", member.Id);
            return new TaskResult<PlanetMember>(false, "An unexpected error occurred.");
        }

        var updated = await ToFullModelAsync(old);
        _hostedPlanetService.GetCached(old.PlanetId)?.UpsertMember(updated);
        _coreHub.NotifyPlanetItemChange(updated);

        return new TaskResult<PlanetMember>(true, "Success", updated);
    }

    public async Task<TaskResult> AddRoleAsync(long planetId, long memberId, long roleId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        if (hostedPlanet.Planet.LockedForMigration)
            return TaskResult.FromFailure(MigrationLock.Message);

        var role = hostedPlanet.GetRoleById(roleId);
        if (role is null)
            return new TaskResult(false, "Role not found.");

        // Use atomic database update to prevent race conditions
        var roleIndex = role.FlagBitIndex;
        var oldMember = await _db.PlanetMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == memberId && x.PlanetId == planetId);
        int updated;

        try
        {
            // Atomic bit set operation - sets the role bit without read-modify-write race
            var query = _db.PlanetMembers.Where(x => x.Id == memberId && x.PlanetId == planetId);

            updated = roleIndex switch
            {
                < 64 => await query.ExecuteUpdateAsync(x =>
                    x.SetProperty(p => p.RoleMembership.Rf0, p => p.RoleMembership.Rf0 | (1L << roleIndex))),
                < 128 => await query.ExecuteUpdateAsync(x =>
                    x.SetProperty(p => p.RoleMembership.Rf1, p => p.RoleMembership.Rf1 | (1L << (roleIndex - 64)))),
                < 192 => await query.ExecuteUpdateAsync(x =>
                    x.SetProperty(p => p.RoleMembership.Rf2, p => p.RoleMembership.Rf2 | (1L << (roleIndex - 128)))),
                _ => await query.ExecuteUpdateAsync(x =>
                    x.SetProperty(p => p.RoleMembership.Rf3, p => p.RoleMembership.Rf3 | (1L << (roleIndex - 192))))
            };
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to add role {RoleId} to member {MemberId}", roleId, memberId);
            return new TaskResult(false, "An unexpected error occurred.");
        }

        if (updated == 0)
            return new TaskResult(false, "Member not found.");

        // ExecuteUpdateAsync bypasses the change tracker, so any tracked entity
        // is now stale. Reload it so subsequent reads see the updated value.
        var tracked = _db.ChangeTracker.Entries<Valour.Database.PlanetMember>()
            .FirstOrDefault(e => e.Entity.Id == memberId);
        if (tracked != null)
            await tracked.ReloadAsync();

        // Fetch updated member for notification
        var member = await _db.PlanetMembers.FindAsync(memberId);
        if (member is not null)
        {
            var model = await ToFullModelAsync(member);
            var cachedPlanet = _hostedPlanetService.GetCached(planetId);
            cachedPlanet?.UpsertMember(model);
            (cachedPlanet ?? hostedPlanet).PermissionCache.ClearCacheForCombo(model.RoleMembership);
            _coreHub.NotifyPlanetItemChange(model);

            if (oldMember is not null)
                await _permissionService.NotifyMemberRoleMembershipChangeAsync(
                    cachedPlanet ?? hostedPlanet,
                    oldMember.ToModel(),
                    model);
        }

        return new TaskResult(true, "Success");
    }

    public async Task<TaskResult> RemoveRoleAsync(long planetId, long memberId, long roleId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        if (hostedPlanet.Planet.LockedForMigration)
            return TaskResult.FromFailure(MigrationLock.Message);

        var role = hostedPlanet.GetRoleById(roleId);
        if (role is null)
            return new TaskResult(false, "Role not found.");

        if (role.IsDefault)
            return new TaskResult(false, "Cannot remove the default role from members.");

        // Use atomic database update to prevent race conditions
        var roleIndex = role.FlagBitIndex;
        var oldMember = await _db.PlanetMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == memberId && x.PlanetId == planetId);
        int updated;

        try
        {
            // Atomic bit clear operation - clears the role bit without read-modify-write race
            var query = _db.PlanetMembers.Where(x => x.Id == memberId && x.PlanetId == planetId);

            updated = roleIndex switch
            {
                < 64 => await query.ExecuteUpdateAsync(x =>
                    x.SetProperty(p => p.RoleMembership.Rf0, p => p.RoleMembership.Rf0 & ~(1L << roleIndex))),
                < 128 => await query.ExecuteUpdateAsync(x =>
                    x.SetProperty(p => p.RoleMembership.Rf1, p => p.RoleMembership.Rf1 & ~(1L << (roleIndex - 64)))),
                < 192 => await query.ExecuteUpdateAsync(x =>
                    x.SetProperty(p => p.RoleMembership.Rf2, p => p.RoleMembership.Rf2 & ~(1L << (roleIndex - 128)))),
                _ => await query.ExecuteUpdateAsync(x =>
                    x.SetProperty(p => p.RoleMembership.Rf3, p => p.RoleMembership.Rf3 & ~(1L << (roleIndex - 192))))
            };
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to remove role {RoleId} from member {MemberId}", roleId, memberId);
            return new TaskResult(false, "An unexpected error occurred.");
        }

        if (updated == 0)
            return new TaskResult(false, "Member not found.");

        // ExecuteUpdateAsync bypasses the change tracker, so any tracked entity
        // is now stale. Reload it so subsequent reads see the updated value.
        var tracked = _db.ChangeTracker.Entries<Valour.Database.PlanetMember>()
            .FirstOrDefault(e => e.Entity.Id == memberId);
        if (tracked != null)
            await tracked.ReloadAsync();

        // Fetch updated member for notification
        var member = await _db.PlanetMembers.FindAsync(memberId);
        if (member is not null)
        {
            var model = await ToFullModelAsync(member);
            var cachedPlanet = _hostedPlanetService.GetCached(planetId);
            cachedPlanet?.UpsertMember(model);
            (cachedPlanet ?? hostedPlanet).PermissionCache.ClearCacheForCombo(model.RoleMembership);
            _coreHub.NotifyPlanetItemChange(model);

            if (oldMember is not null)
                await _permissionService.NotifyMemberRoleMembershipChangeAsync(
                    cachedPlanet ?? hostedPlanet,
                    oldMember.ToModel(),
                    model);
        }

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Soft deletes the PlanetMember (and member roles)
    /// </summary>
    public async Task<TaskResult> DeleteAsync(
        long memberId,
        bool doTransaction = true,
        bool bypassMigrationLock = false)
    {
        IDbContextTransaction trans = null;
        
        if (doTransaction)
        {
            trans = await _db.Database.BeginTransactionAsync();
        }

        var dbMember = await _db.PlanetMembers.FindAsync(memberId);

        if (dbMember is null)
            return new TaskResult(false, "Member not found");

        if (!bypassMigrationLock)
        {
            var migrationGuard = await MigrationLock.GuardAsync(_db, dbMember.PlanetId);
            if (!migrationGuard.Success)
                return migrationGuard;
        }
        
        try
        {
            // Remove roles
            var channelStates = await _db.UserChannelStates.Where(x => x.UserId == dbMember.UserId && x.PlanetId == dbMember.PlanetId).ToListAsync();
            _db.UserChannelStates.RemoveRange(channelStates);
            
            dbMember.IsDeleted = true;
            dbMember.RoleMembership = PlanetRoleMembership.Default; // Default role

            await _db.SaveChangesAsync();
            
            if (trans is not null) 
            {
                await trans.CommitAsync();
            }
        }
        catch (System.Exception e)
        {
            _logger.LogError("Critical error deleting member!\n {e}", e);
            
            if (trans is not null)
            {
                await trans.RollbackAsync();
            }

            return new(false, "An unexpected error occurred.");
        }

        MemberIdLookup.Remove((dbMember.UserId, dbMember.PlanetId), out _);
        _hostedPlanetService.GetCached(dbMember.PlanetId)?.RemoveMember(memberId);

        await _coreHub.EvictUserFromPlanetRealtimeAsync(dbMember.PlanetId, dbMember.UserId);
        _coreHub.NotifyPlanetItemDelete(await ToFullModelAsync(dbMember));

        return TaskResult.SuccessResult;
    }
}
