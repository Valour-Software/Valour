using Valour.Server.Database;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Authorization;

namespace Valour.Server.Services;

public class PlanetMemberService
{
    private readonly ValourDB _db;
    private readonly PlanetService _planetService;
    private readonly PermissionsService _permissionsService;

    public PlanetMemberService(
        ValourDB db, 
        PlanetService planetService, 
        PermissionsService permService)
    {
        _db = db;
        _planetService = planetService;
        _permissionsService = permService;
    }

    /// <summary>
    /// Returns the PlanetMember for the given id
    /// </summary>
    public async ValueTask<PlanetMember> GetAsync(long id) =>
        await _db.PlanetMembers.FindAsync(id);
    
    /// <summary>
    /// Returns the deleted PlanetMember for the given id
    /// Null if there is no deleted member with that id
    /// </summary>
    public async ValueTask<PlanetMember> GetIncludingDeletedAsync(long id) =>
        await _db.PlanetMembers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
    
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
    public async Task<PlanetMember> GetByUserAsync(long userId, long planetId) =>
        await _db.PlanetMembers.FirstOrDefaultAsync(x => x.PlanetId == planetId && x.UserId == userId);
    
    /// <summary>
    /// Returns the deleted PlanetMember for a given user id and planet id
    /// Null if there is no deleted member with the ids
    /// </summary>
    public async Task<PlanetMember> GetIncludingDeletedByUserAsync(long userId, long planetId) =>
        await _db.PlanetMembers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.PlanetId == planetId && x.UserId == userId);

    /// <summary>
    /// Returns the roles for the given PlanetMember id
    /// </summary>
    public async Task<List<PlanetRole>> GetRolesAsync(PlanetMember member)
    {
        member.Roles ??= await _db.PlanetRoleMembers.Where(x => x.MemberId == member.Id)
            .Include(x => x.Role)
            .OrderBy(x => x.Role.Position)
            .Select(x => x.Role)
            .ToListAsync();

        return member.Roles;
    }

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
    public async Task<PlanetRole> GetPrimaryRoleAsync(PlanetMember member)
    {
        return (await GetRolesAsync(member)).FirstOrDefault();
    }

    /// <summary>
    /// Returns if the member has the given planet permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetMember member, PlanetPermission perm) =>
        await _permissionsService.HasPermissionAsync(member, perm);

    /// <summary>
    /// Returns the authority of a planet member
    /// </summary>
    public async Task<int> GetAuthorityAsync(PlanetMember member)
    {
        var planet = await member.GetPlanetAsync(_planetService);
        
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
    /// Deletes the PlanetMember
    /// Note: This is a soft deletion
    /// </summary>
    public void Delete(PlanetMember member)
    {
        // Remove roles
        var roles = _db.PlanetRoleMembers.Where(x => x.MemberId == member.Id);
        _db.PlanetRoleMembers.RemoveRange(roles);

        // Soft delete member
        member.IsDeleted = true;
        _db.PlanetMembers.Update(member);
    }
}