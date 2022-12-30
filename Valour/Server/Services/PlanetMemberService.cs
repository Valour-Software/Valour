using Microsoft.EntityFrameworkCore.Storage;
using Valour.Database.Context;
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

    public PlanetMemberService(
        ValourDB db,
        CoreHubService coreHub,
        TokenService tokenService)
    {
        _db = db;
        _coreHub = coreHub;
        _tokenService = tokenService;
    }
    
    /// <summary>
    /// Returns the PlanetMember for the given id
    /// </summary>
    public async Task<PlanetMember> GetAsync(long id) =>
        (await _db.PlanetMembers.FindAsync(id)).ToModel();
    
    /// <summary>
    /// Returns the current user's PlanetMember for the given planet id
    /// </summary>
    public async Task<PlanetMember> GetCurrentAsync(long planetId) => 
        (await _db.PlanetMembers.FindAsync(planetId, (await _tokenService.GetCurrentToken()).Id)).ToModel();

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
    /// Returns the PlanetMember for a given user id and planet id
    /// </summary>
    public async Task<Models.PlanetMember> GetByUserAsync(long userId, long planetId) =>
        (await _db.PlanetMembers.FirstOrDefaultAsync(x => x.PlanetId == planetId && x.UserId == userId)).ToModel();

    /// <summary>
    /// Returns the roles for the given PlanetMember id
    /// </summary>
    public async Task<List<PlanetRole>> GetRolesAsync(PlanetMember member) =>
        await _db.PlanetRoleMembers.Where(x => x.MemberId == member.Id)
            .Include(x => x.Role)
            .OrderBy(x => x.Role.Position)
            .Select(x => x.Role.ToModel())
            .ToListAsync();

    /// <summary>
    /// Returns the roles for the given PlanetMember id,
    /// including the permissions nodes for a specific target channel
    /// </summary>
    public async Task<List<PlanetRole>> GetRolesAndNodesAsync(PlanetMember member, long targetId, PermissionsTargetType type) =>
        await _db.PlanetRoleMembers.Where(x => x.MemberId == member.Id)
            .Include(x => x.Role)
            .ThenInclude(r => r.PermissionNodes.Where(n => n.TargetId == targetId && n.TargetType == type))
            .OrderBy(x => x.Role.Position)
            .Select(x => x.Role)
            .ToListAsync();

    /// <summary>
    /// Returns the primary (top) role for the given PlanetMember id
    /// </summary>
    public async Task<PlanetRole> GetPrimaryRoleAsync(PlanetMember member) =>
        (await GetRolesAsync(member)).FirstOrDefault();

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
        var mainRole = await GetPrimaryRoleAsync(member);

        // Return permission state
        return mainRole.HasPermission(permission);
    }
    
    /// <summary>
    /// Returns if the member has the given channel permission
    /// </summary>
    public async ValueTask<bool> HasPermissionAsync(PlanetMember member, PlanetChannel channel, Permission permission)
    {
        var planet = await channel.GetPlanetAsync(_planetService);

        if (planet.OwnerId == member.UserId)
            return true;

        // If the channel inherits from its parent, move up until it does not
        while (channel.InheritsPerms)
        {
            channel = await _categoryService.GetAsync(channel.ParentId.Value);
        }
        
        // Load permission data
        // This loads the roles and the node for the specific channel
        var roles = await GetRolesAndNodesAsync(member, channel.Id, permission.TargetType);

        // Starting from the most important role, we stop once we hit the first clear "TRUE/FALSE".
        // If we get an undecided, we continue to the next role down
        foreach (var role in roles)
        {
            // If the role has a node for this channel, we use that
            var node = role.PermissionNodes.FirstOrDefault();
            
            // We continue to the next role
            // if the node is null
            if (node is null)
                continue;

            // If there is no view permission, there can't be any other permissions
            // View is always 0x01 for chanel permissions, so it is safe to use ChatChannelPermission.View for
            // all cases.
            if (node.GetPermissionState(ChatChannelPermissions.View) == PermissionState.False)
                return false;

            var state = node.GetPermissionState(permission);

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

        // Fallback to default permissions
        return Permission.HasPermission(permission.GetDefault(), permission);
    }
    
    /// <summary>
    /// Adds the given user to the given planet as a member
    /// </summary>
    public async Task<TaskResult<PlanetMember>> AddMemberAsync(Planet planet, User user, bool doTransaction)
    {
        if (await _db.PlanetBans.AnyAsync(x => x.TargetId == user.Id && x.PlanetId == planet.Id &&
            (x.TimeExpires != null && x.TimeExpires > DateTime.UtcNow)))
        {
            return new TaskResult<PlanetMember>(false, "You are banned from this planet.");
        }

        // New member
        PlanetMember member;
        
        // See if there is an old member
        var oldMember = await GetIncludingDeletedByUserAsync(user.Id, planet.Id);
        
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
            member = new PlanetMember()
            {
                Id = IdManager.Generate(),
                Nickname = user.Name,
                PlanetId = planet.Id,
                UserId = user.Id
            };
        }

        // Add to default planet role
        var roleMember = new PlanetRoleMember()
        {
            Id = IdManager.Generate(),
            PlanetId = planet.Id,
            UserId = user.Id,
            RoleId = planet.DefaultRoleId,
            MemberId = member.Id
        };
        
        IDbContextTransaction trans = null;

        if (doTransaction)
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
        catch (System.Exception e)
        {
            await trans.RollbackAsync();
            return new TaskResult<PlanetMember>(false, e.Message);
        }

        if (doTransaction)
            await trans.CommitAsync();

        _coreHub.NotifyPlanetItemChange(member);

        Console.WriteLine($"User {user.Name} ({user.Id}) has joined {planet.Name} ({planet.Id})");

        return new TaskResult<PlanetMember>(true, "Success", member);
    }

    /// <summary>
    /// Soft deletes the PlanetMember (and member roles)
    /// </summary>
    public async Task DeleteAsync(PlanetMember member)
    {
        // Remove roles
        var roles = _db.PlanetRoleMembers.Where(x => x.MemberId == member.Id);
        _db.PlanetRoleMembers.RemoveRange(roles);

        // Soft delete member
        member.IsDeleted = true;
        _db.PlanetMembers.Update(member);
        await _db.SaveChangesAsync();
    }
}