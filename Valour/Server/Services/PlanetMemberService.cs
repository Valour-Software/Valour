using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class PlanetMemberService
{
    private readonly ValourDB _db;
    private readonly CoreHubService _coreHub;
    private readonly TokenService _tokenService;
    private readonly ILogger<PlanetMemberService> _logger;

    public PlanetMemberService(
        ValourDB db,
        CoreHubService coreHub,
        TokenService tokenService,
        ILogger<PlanetMemberService> logger)
    {
        _db = db;
        _coreHub = coreHub;
        _tokenService = tokenService;
        _logger = logger;
    }
    
    /// <summary>
    /// Returns the PlanetMember for the given id
    /// </summary>
    public async Task<PlanetMember> GetAsync(long id) =>
        (await _db.PlanetMembers.FindAsync(id)).ToModel();

    /// <summary>
    /// Returns the current user's PlanetMember for the given planet id
    /// </summary>
    public async Task<PlanetMember> GetCurrentAsync(long planetId)
    {
        var token = await _tokenService.GetCurrentToken();
        if (token is null)
            return null;
        
        return (await _db.PlanetMembers.FirstOrDefaultAsync(x => x.PlanetId == planetId && 
                                                                             x.UserId == token.UserId))
            .ToModel();
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
        var token = await _tokenService.GetCurrentToken();
        if (token is null)
            return false;
        
        return await _db.PlanetMembers.AnyAsync(x => x.UserId == token.UserId && x.PlanetId == planetId);
    }

    /// <summary>
    /// Returns the PlanetMember for a given user id and planet id
    /// </summary>
    public async Task<Models.PlanetMember> GetByUserAsync(long userId, long planetId) =>
        (await _db.PlanetMembers.FirstOrDefaultAsync(x => x.PlanetId == planetId && x.UserId == userId)).ToModel();

    /// <summary>
    /// Returns the roles for the given member id
    /// </summary>
    public async Task<List<PlanetRole>> GetRolesAsync(long memberId) =>
        await _db.PlanetRoleMembers.Where(x => x.MemberId == memberId)
            .Include(x => x.Role)
            .OrderBy(x => x.Role.Position)
            .Select(x => x.Role.ToModel())
            .ToListAsync();
    
    /// <summary>
    /// Returns the role ids for the given member id
    /// </summary>
    public async Task<List<long>> GetRoleIdsAsync(long memberId) =>
        await _db.PlanetRoleMembers.Where(x => x.MemberId == memberId)
            .Include(x => x.Role)
            .OrderBy(x => x.Role.Position)
            .Select(x => x.Role.Id)
            .ToListAsync();

    
    public class PlanetRoleAndNode
    {
        public PlanetRole Role { get; set; }
        public PermissionsNode Node { get; set; }
    }

    public class PlanetRoleIdAndNode
    {
        public long RoleId { get; set; }
        public PermissionsNode Node { get; set;  }
    }
    
    /// <summary>
    /// Returns the roles for the given PlanetMember id,
    /// including the permissions node for a specific target channel
    /// </summary>
    public async Task<List<PlanetRoleAndNode>> GetRolesAndNodesAsync(PlanetMember member, long targetId, PermChannelType type) =>
       await _db.PlanetRoleMembers.Where(x => x.MemberId == member.Id)
            .Include(x => x.Role)
            .ThenInclude(r => r.PermissionNodes)
            .OrderBy(x => x.Role.Position)
            .Select(x => new PlanetRoleAndNode()
            {
                Role = x.Role.ToModel(),
                Node = x.Role.PermissionNodes.FirstOrDefault(n => n.TargetId == targetId && n.TargetType == type).ToModel()
            })
            .ToListAsync();

    /// <summary>
    /// Returns the role ids for the given PlanetMember id,
    /// including the permissions node for a specific target channel
    /// this will return role ids that no node
    /// </summary>
    public async Task<List<PlanetRoleIdAndNode>> GetRoleIdsAndNodesAsync(PlanetMember member, long targetId, PermChannelType type) =>
        await _db.PlanetRoleMembers.Where(x => x.MemberId == member.Id)
            .Include(x => x.Role)
            .ThenInclude(r => r.PermissionNodes.Where(n => n.TargetId == targetId && n.TargetType == type))
            .OrderBy(x => x.Role.Position)
            .Select(x => new PlanetRoleIdAndNode()
            {
                RoleId = x.Role.Id,
                Node = x.Role.PermissionNodes.FirstOrDefault(n => n.TargetId == targetId && n.TargetType == type).ToModel()
            })
            .Where(x => x.Node.TargetId == targetId && x.Node.TargetType == type)
            .ToListAsync();

    /// <summary>
    /// Returns the permission nodes for the given target in order of role position
    /// </summary>
    public async Task<List<PermissionsNode>> GetPermNodesAsync(PlanetMember member, long targetId, PermChannelType type) =>
        await _db.PlanetRoleMembers.Where(x => x.MemberId == member.Id)
            .Include(x => x.Role)
            .ThenInclude(r => r.PermissionNodes.Where(n => n.TargetId == targetId && n.TargetType == type))
            .OrderBy(x => x.Role.Position)
            .Select(x => x.Role.PermissionNodes.FirstOrDefault().ToModel())
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
    /// Returns the authority of a planet member
    /// </summary>
    public async Task<int> GetAuthorityAsync(PlanetMember member)
    {
        var planet = await _db.Planets.FindAsync(member.PlanetId);
        
        // Planet owner has highest possible authority
        if (planet.OwnerId == member.UserId)
            return int.MaxValue;
        
        // Otherwise, we get the primary role's position
        var rolePos = await _db.PlanetRoleMembers.Where(x => x.MemberId == member.Id)
            .Include(x => x.Role)
            .OrderBy(x => x.Role.Position)
            .Select(x => x.Role.Position)
            .FirstAsync();
        
        // Calculate the authority
        return int.MaxValue - rolePos - 1;
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

        // Get user main role
        var mainRole = await GetPrimaryRoleAsync(member.Id);

        // Return permission state
        return mainRole.HasPermission(permission);
    }

    /// <summary>
    /// Returns if the member has the given channel permission
    /// </summary>
    public async ValueTask<bool> HasPermissionAsync(PlanetMember member, PlanetChannel target, Permission permission)
    {
        if (target is null)
            return false;
        
        var planet = await _db.Planets.FindAsync(target.PlanetId);
        // Fail if the planet does not exist
        if (planet is null)
            return false;

        if (planet.OwnerId == member.UserId)
            return true;

        // If the channel inherits from its parent, move up until it does not
        while (target.InheritsPerms && target.ParentId is not null)
        {
            target = (await _db.PlanetCategories.FindAsync(target.ParentId.Value)).ToModel();
        }
        
        // Get permission nodes in order of role position
        var rolePermData = await GetRolesAndNodesAsync(member, target.Id, permission.TargetType);

        var viewPerm = PermissionState.Undefined;

        foreach (var rolePerm in rolePermData)
        {
            var node = rolePerm.Node;
            if (node is null)
                continue;

            viewPerm = node.GetPermissionState(ChatChannelPermissions.View, true);
            if (viewPerm != PermissionState.Undefined)
                break;
        }

        if (viewPerm == PermissionState.Undefined)
        {
            var _topRole = rolePermData.FirstOrDefault()?.Role ?? PlanetRole.DefaultRole;
            viewPerm = Permission.HasPermission(_topRole.ChatPermissions, ChatChannelPermissions.View) ? PermissionState.True : PermissionState.False;
        }

        if (viewPerm != PermissionState.True)
            return false;

        // Starting from the most important role, we stop once we hit the first clear "TRUE/FALSE".
        // If we get an undecided, we continue to the next role down
        foreach (var rolePerm in rolePermData)
        {
            var node = rolePerm.Node;

            if (node is null)
                continue;
            // If there is no view permission, there can't be any other permissions
            // View is always 0x01 for channel permissions, so it is safe to use ChatChannelPermission.View for
            // all cases.

            var state = node.GetPermissionState(permission, true);

            switch (state)
            {
                case PermissionState.Undefined:
                    continue;
                case PermissionState.True:
                    return true;
                case PermissionState.False:
                default:
                    return false;
            }
        }

        var topRole = rolePermData.FirstOrDefault()?.Role ?? PlanetRole.DefaultRole;

        // Fallback to base permissions
        switch (permission)
        {
            case ChatChannelPermission:
                return Permission.HasPermission(topRole.ChatPermissions, permission);
            case CategoryPermission:
                return Permission.HasPermission(topRole.CategoryPermissions, permission);
            case VoiceChannelPermission:
                return Permission.HasPermission(topRole.VoicePermissions, permission);
            default:
                throw new Exception("Unexpected permission type: " + permission.GetType().Name);
        }
    }
    
    /// <summary>
    /// Adds the given user to the given planet as a member
    /// </summary>
    public async Task<TaskResult<PlanetMember>> AddMemberAsync(Planet planet, User user)
    {
        if (planet is null)
            return new TaskResult<PlanetMember>(false, "Planet not found.");

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
                UserId = user.Id
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
        var old = await _db.PlanetMembers.FindAsync(member.Id);

        if (old is null)
            return new TaskResult<PlanetMember>(false, "Member not found.");
        
        if (old.PlanetId != member.PlanetId)
            return new TaskResult<PlanetMember>(false, "Cannot change planet of member.");
        
        if (old.UserId != member.UserId)
            return new TaskResult<PlanetMember>(false, "Cannot change user of member.");

        if (old.MemberPfp != member.MemberPfp)
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
        
        try
        {
            await _db.PlanetRoleMembers.AddAsync(newRoleMember);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            return new TaskResult<PlanetRoleMember>(false, "An unexpected error occurred.");
        }

        var newMemberModel = newRoleMember.ToModel();
        
        _coreHub.NotifyPlanetItemChange(newMemberModel);

        return new TaskResult<PlanetRoleMember>(true, "Success", newMemberModel);
    }
    
    public async Task<TaskResult> RemoveRoleAsync(long memberId, long roleId)
    {
        var roleMember = await _db.PlanetRoleMembers.FirstOrDefaultAsync(x => x.MemberId == memberId && x.RoleId == roleId);
        
        if (roleMember is null)
            return new TaskResult(false, "Member does not have this role.");

        var isDefaultRole = await _db.PlanetRoles.AnyAsync(x => x.Id == roleMember.RoleId && x.IsDefault);
        if (isDefaultRole)
            return new TaskResult(false, "Cannot remove the default role from members.");
        
        try
        {
            _db.PlanetRoleMembers.Remove(roleMember);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            return new TaskResult(false, "An unexpected error occurred.");
        }

        _coreHub.NotifyPlanetItemDelete(roleMember.ToModel());

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Soft deletes the PlanetMember (and member roles)
    /// </summary>
    public async Task<TaskResult> DeleteAsync(PlanetMember member)
    {
        await using var trans = await _db.Database.BeginTransactionAsync();

        try
        {
            // Remove roles
            var roles = _db.PlanetRoleMembers.Where(x => x.MemberId == member.Id);
            _db.PlanetRoleMembers.RemoveRange(roles);

            Valour.Database.PlanetMember dbMember = new() { Id = member.Id, IsDeleted = true };
            _db.PlanetMembers.Attach(dbMember).Property(x => x.IsDeleted).IsModified = true;

            await _db.SaveChangesAsync();
            await trans.CommitAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError("Critical error deleting member.", e);
            await trans.RollbackAsync();
            return new(false, "An unexpected error occurred.");
        }

        _coreHub.NotifyPlanetItemDelete(member);

        return TaskResult.SuccessResult;
    }
}