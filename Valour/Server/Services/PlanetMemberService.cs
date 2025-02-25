using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore.Storage;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class PlanetMemberService
{
    private readonly ValourDb _db;
    private readonly CoreHubService _coreHub;
    private readonly TokenService _tokenService;
    private readonly PlanetPermissionService _permissionService;
    private readonly HostedPlanetService _hostedPlanetService;
    private readonly ILogger<PlanetMemberService> _logger;
    
    private static readonly ConcurrentDictionary<(long, long), long> MemberIdLookup = new();

    /// <summary>
    /// A cached current member. It may not match the planet being requested (so check!) but it's
    /// highly likely to be the same, so it makes sense to cache it.
    /// </summary>
    private PlanetMember _currentMember;
    
    public PlanetMemberService(
        ValourDb db,
        CoreHubService coreHub,
        TokenService tokenService,
        ILogger<PlanetMemberService> logger, 
        PlanetPermissionService permissionService, 
        HostedPlanetService hostedPlanetService)
    {
        _db = db;
        _coreHub = coreHub;
        _tokenService = tokenService;
        _logger = logger;
        _permissionService = permissionService;
        _hostedPlanetService = hostedPlanetService;
    }

    /// <summary>
    /// Returns the PlanetMember for the given id
    /// </summary>
    public async Task<PlanetMember> GetAsync(long id)
    {
        var member = await _db.PlanetMembers
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id);
        
        return member.ToModel();
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
        if (MemberIdLookup.TryGetValue((userId, planetId), out var memberId))
        {
            var member = await _db.PlanetMembers
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.Id == memberId);
            return member.ToModel();
        }
        else
        {
            var member = await _db.PlanetMembers
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.PlanetId == planetId && x.UserId == userId);
            
            if (member is not null)
            {
                MemberIdLookup.TryAdd((userId, planetId), member.Id);
            }
            
            return member.ToModel();
        }
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

        return await _permissionService.HasChannelPermissionAsync(member, target, permission);
    }
    
    /// <summary>
    /// Adds the given user to the given planet as a member
    /// </summary>
    public async Task<TaskResult<PlanetMember>> AddMemberAsync(long planetId, long userId)
    {
        var planet = await _db.Planets.FindAsync(planetId);
        if (planet is null)
            return new TaskResult<PlanetMember>(false, "Planet not found.");

        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return new TaskResult<PlanetMember>(false, "User not found.");

        if (await _db.PlanetBans.AnyAsync(x => x.TargetId == user.Id && x.PlanetId == planet.Id &&
            (x.TimeExpires != null && x.TimeExpires > DateTime.UtcNow)))
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
                Nickname = user.Name,
                PlanetId = planet.Id,
                UserId = user.Id,
                User = user,
                
                Rf0 = 0x01, // First bit (position 0) is the default role
            };
        }
        
        IDbContextTransaction trans = null;

        trans = await _db.Database.BeginTransactionAsync();

        try
        {
            if (rejoin)
            {
                member.IsDeleted = false;
                
                // Reset roles
                member.Rf0 = 0x01;
                member.Rf1 = 0;
                member.Rf2 = 0;
                member.Rf3 = 0;
                
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
            await trans.RollbackAsync();
            return new TaskResult<PlanetMember>(false, e.Message);
        }

        await trans.CommitAsync();

        var model = member.ToModel();

        _coreHub.NotifyPlanetItemChange(model);

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

        if (old.MemberAvatar != member.MemberAvatar)
            return new TaskResult<PlanetMember>(false, "Profile image can only be changed via cdn.");

        var nameValid = ISharedPlanetMember.ValidateName(member);
        if (!nameValid.Success)
            return new TaskResult<PlanetMember>(false, nameValid.Message);

        try
        {
            _db.Entry(old).CurrentValues.SetValues(member);
            _db.PlanetMembers.Update(old);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            return new TaskResult<PlanetMember>(false, "An unexpected error occurred.");
        }
        
        _coreHub.NotifyPlanetItemChange(member);

        // ensure user model is included
        member.User = old.User.ToModel();

        return new TaskResult<PlanetMember>(true, "Success", member);
    }

    public async Task<TaskResult> AddRoleAsync(long planetId, long memberId, long roleId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        
        var member = await _db.PlanetMembers.FindAsync(memberId);
        if (member is null)
            return new TaskResult(false, "Member not found.");

        var role = hostedPlanet.GetRoleById(roleId);
        if (role is null)
            return new TaskResult(false, "Role not found.");
        
        if (member.PlanetId != role.PlanetId)
            return new TaskResult(false, "Role and member are not in the same planet.");

        if (await HasRoleAsync(member, roleId))
            return new TaskResult(false, "Member already has this role.");

        try
        {
            // Add role to member
            member.RoleMembership = member.RoleMembership.AddRole(role);
            await _db.SaveChangesAsync();
        } 
        catch (Exception e)
        {
            return new TaskResult(false, "An unexpected error occurred.");
        }

        _coreHub.NotifyPlanetItemChange(member.ToModel());

        return new TaskResult(true, "Success");
    }
    
    public async Task<TaskResult> RemoveRoleAsync(long planetId, long memberId, long roleId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        
        var member = await _db.PlanetMembers.FindAsync(memberId);
        if (member is null)
            return new TaskResult(false, "Member not found.");

        var role = hostedPlanet.GetRoleById(roleId);
        if (role is null)
            return new TaskResult(false, "Role not found.");
        
        if (role.IsDefault)
            return new TaskResult(false, "Cannot remove the default role from members.");

        if (member.PlanetId != role.PlanetId)
            return new TaskResult(false, "Role and member are not in the same planet.");

        try
        {
            // Remove role from member
            member.RoleMembership = member.RoleMembership.RemoveRole(role);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            return new TaskResult(false, "An unexpected error occurred.");
        }

        _coreHub.NotifyPlanetItemChange(member.ToModel());

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Soft deletes the PlanetMember (and member roles)
    /// </summary>
    public async Task<TaskResult> DeleteAsync(long memberId, bool doTransaction = true)
    {
        IDbContextTransaction trans = null;
        
        if (doTransaction)
        {
            trans = await _db.Database.BeginTransactionAsync();
        }

        var dbMember = await _db.PlanetMembers.FindAsync(memberId);

        if (dbMember is null)
            return new TaskResult(false, "Member not found");
        
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

        _coreHub.NotifyPlanetItemDelete(dbMember.ToModel());

        return TaskResult.SuccessResult;
    }
}