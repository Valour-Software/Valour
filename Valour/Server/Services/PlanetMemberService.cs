using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore.Storage;
using Valour.Database;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Channel = Valour.Server.Models.Channel;
using PermissionsNode = Valour.Server.Models.PermissionsNode;
using PlanetMember = Valour.Server.Models.PlanetMember;
using PlanetRole = Valour.Server.Models.PlanetRole;
using PlanetRoleMember = Valour.Server.Models.PlanetRoleMember;

namespace Valour.Server.Services;

public class PlanetMemberService
{
    private readonly ValourDb _db;
    private readonly CoreHubService _coreHub;
    private readonly TokenService _tokenService;
    private readonly PlanetPermissionService _permissionService;
    private readonly ILogger<PlanetMemberService> _logger;
    
    private static readonly ConcurrentDictionary<(long, long), long> MemberIdLookup = new();

    public PlanetMemberService(
        ValourDb db,
        CoreHubService coreHub,
        TokenService tokenService,
        ILogger<PlanetMemberService> logger, 
        PlanetPermissionService permissionService)
    {
        _db = db;
        _coreHub = coreHub;
        _tokenService = tokenService;
        _logger = logger;
        _permissionService = permissionService;
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
        var token = await _tokenService.GetCurrentTokenAsync();
        if (token is null)
            return null;

        return await GetByUserAsync(token.UserId, planetId);
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
    public async Task<List<PlanetRole>> GetRolesAsync(long memberId) =>
        await _db.PlanetRoleMembers
            .AsNoTracking()
            .Where(x => x.MemberId == memberId)
            .Include(x => x.Role)
            .OrderBy(x => x.Role.Position)
            .Select(x => x.Role.ToModel())
            .ToListAsync();
    
    /// <summary>
    /// Returns the role ids for the given member id
    /// </summary>
    public async Task<List<long>> GetRoleIdsAsync(long memberId) =>
        await _db.PlanetRoleMembers
            .AsNoTracking()
            .Where(x => x.MemberId == memberId)
            .Include(x => x.Role)
            .OrderBy(x => x.Role.Position)
            .Select(x => x.Role.Id)
            .ToListAsync();
    
    /// <summary>
    /// Returns the primary (top) role for the given PlanetMember id
    /// </summary>
    public async Task<PlanetRole> GetPrimaryRoleAsync(long memberId) =>
        (await GetRolesAsync(memberId)).FirstOrDefault();
    
    /// <summary>
    /// Returns if the given memberid has the given role
    /// </summary>
    public async Task<bool> HasRoleAsync(long memberId, long roleId) =>
        await _db.PlanetRoleMembers.AnyAsync(x => x.MemberId == memberId && x.RoleId == roleId);


    /// <summary>
    /// Returns if the given member is an admin
    /// </summary>
    public async Task<bool> IsAdminAsync(long memberId)
    {
        var member = await _db.PlanetMembers.FindAsync(memberId);
        if (member is null)
            return false;
        
        if (await _db.PlanetRoleMembers.AnyAsync(x => x.MemberId == memberId && x.Role.IsAdmin))
        {
            return true;
        }

        var planet = await _db.Planets.FindAsync(member.PlanetId);
        if (planet is null)
            return false; // This should be impossible

        // Owner is always admin - last check
        return (planet.OwnerId == member.UserId);
    }
        
        
    
    /// <summary>
    /// Returns the authority of a planet member
    /// </summary>
    public async Task<uint> GetAuthorityAsync(PlanetMember member)
    {
        return await _permissionService.GetAuthorityAsync(member);
    }
    
    /// <summary>
    /// Returns if a member has the given permission
    /// </summary>
    public async ValueTask<bool> HasPermissionAsync(PlanetMember member, PlanetPermission permission)
    {
        if (member is null)
            return false;
        
        // Special case for viewing planets
        // All existing members can view a planet
        if (permission.Value == PlanetPermissions.View.Value)
        {
            return true;
        }
        
        var planet = await _db.Planets.FindAsync(member.PlanetId);

        // This should never happen, but we will still check
        if (planet is null)
            return false;
        
        // Owner has all permissions
        if (member.UserId == planet.OwnerId)
            return true;

        return await _db.PlanetRoleMembers
            .AsNoTracking()
            .Where(x => x.MemberId == member.Id)
            .Include(x => x.Role)
            .AnyAsync(x => x.Role.IsAdmin || // Admins have all permissions
                      (x.Role.Permissions & permission.Value) != 0); // Otherwise, at least one role must have the planet permission granted
    }

    /// <summary>
    /// Returns if the member has the given channel permission
    /// </summary>
    public async ValueTask<bool> HasPermissionAsync(PlanetMember member, Channel target, Permission permission)
    {
        if (target is null)
            return false;
        
        // If not planet channel, this doesn't apply
        if (!ISharedChannel.PlanetChannelTypes.Contains(target.ChannelType))
        {
            return true;
        }
        
        
        
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
                User = user
            };
        }
        
        var defaultRoleId = await _db.PlanetRoles.Where(x => x.PlanetId == planet.Id && x.IsDefault)
            .Select(x => x.Id)
            .FirstOrDefaultAsync();

        // Add to default planet role
        var roleMember = new Valour.Database.PlanetRoleMember()
        {
            Id = IdManager.Generate(),
            PlanetId = planet.Id,
            UserId = user.Id,
            RoleId = defaultRoleId,
            MemberId = member.Id
        };
        
        IDbContextTransaction trans = null;

        trans = await _db.Database.BeginTransactionAsync();

        try
        {
            if (rejoin)
            {
                member.IsDeleted = false;
                _db.PlanetMembers.Update(member);
            }
            else
            {
                await _db.PlanetMembers.AddAsync(member);
            }
            
            await _db.PlanetRoleMembers.AddAsync(roleMember);
            await _db.SaveChangesAsync();
            
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

    public async Task<TaskResult<PlanetRoleMember>> AddRoleAsync(long memberId, long roleId)
    {
        var member = await _db.PlanetMembers.FindAsync(memberId);
        if (member is null)
            return new TaskResult<PlanetRoleMember>(false, "Member is null.");

        var role = await _db.PlanetRoles.FindAsync(roleId);
        if (role is null)
            return new TaskResult<PlanetRoleMember>(false, "Role is null.");
        
        if (role.IsOwner)
            return new TaskResult<PlanetRoleMember>(false, "Cannot add owner role to member.");
        
        if (member.PlanetId != role.PlanetId)
            return new TaskResult<PlanetRoleMember>(false, "Role and member are not in the same planet.");

        if (await HasRoleAsync(memberId, roleId))
            return new TaskResult<PlanetRoleMember>(false, "Member already has this role.");
        
        Valour.Database.PlanetRoleMember newRoleMember = new()
        {
            Id = IdManager.Generate(),
            MemberId = memberId,
            RoleId = roleId,
            UserId = member.UserId,
            PlanetId = member.PlanetId
        };

        await using var trans = await _db.Database.BeginTransactionAsync();
        
        try
        {
            await _db.PlanetRoleMembers.AddAsync(newRoleMember);
            await _db.SaveChangesAsync();

            await _accessService.UpdateAllChannelAccessMember(memberId);
            await _db.SaveChangesAsync();
            
            await trans.CommitAsync();
        }
        catch (Exception e)
        {
            await trans.RollbackAsync();
            return new TaskResult<PlanetRoleMember>(false, "An unexpected error occurred.");
        }

        var newMemberModel = newRoleMember.ToModel();
        
        _coreHub.NotifyPlanetItemChange(newMemberModel);

        return new TaskResult<PlanetRoleMember>(true, "Success", newMemberModel);
    }
    
    public async Task<TaskResult> RemoveRoleAsync(long memberId, long roleId)
    {
        var roleMember = await _db.PlanetRoleMembers.Include(x => x.Role).FirstOrDefaultAsync(x => x.MemberId == memberId && x.RoleId == roleId);
        
        if (roleMember is null)
            return new TaskResult(false, "Member does not have this role.");
        
        if (roleMember.Role.IsDefault)
            return new TaskResult(false, "Cannot remove the default role from members.");
        
        if (roleMember.Role.IsOwner)
            return new TaskResult(false, "Cannot remove the owner role.");
        
        await using var trans = await _db.Database.BeginTransactionAsync();
        
        try
        {
            _db.PlanetRoleMembers.Remove(roleMember);
            await _db.SaveChangesAsync();
            
            await _accessService.UpdateAllChannelAccessMember(memberId);
            await _db.SaveChangesAsync();
            
            await trans.CommitAsync();
        }
        catch (Exception e)
        {
            await trans.RollbackAsync();
            return new TaskResult(false, "An unexpected error occurred.");
        }

        _coreHub.NotifyPlanetItemDelete(roleMember.ToModel());

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
            var roles = _db.PlanetRoleMembers.Where(x => x.MemberId == memberId);
            _db.PlanetRoleMembers.RemoveRange(roles);
            
            dbMember.IsDeleted = true;

            await _db.SaveChangesAsync();
            
            // Remove channel access
            await _accessService.ClearMemberAccessAsync(dbMember.Id);
            
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