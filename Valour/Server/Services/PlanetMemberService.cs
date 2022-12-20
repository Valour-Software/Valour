using Valour.Server.Database;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Authorization;

namespace Valour.Server.Services;

public class PlanetMemberService
{
    private readonly ValourDB _db;
    private readonly PermissionsService _permissionsService;

    public PlanetMemberService(ValourDB db, PermissionsService permService)
    {
        _db = db;
        _permissionsService = permService;
    }

    /// <summary>
    /// Returns the PlanetMember for the given id
    /// </summary>
    public async ValueTask<PlanetMember> GetAsync(long id) =>
        await _db.PlanetMembers.FindAsync(id);
    
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
    /// Returns the roles for the given PlanetMember id
    /// </summary>
    public async Task<List<PlanetRole>> GetRolesAsync(long id) =>
        await _db.PlanetRoleMembers.Where(x => x.MemberId == id)
            .Include(x => x.Role)
            .OrderBy(x => x.Role.Position)
            .Select(x => x.Role)
            .ToListAsync();
    
    /// <summary>
    /// Returns the roles for the given PlanetMember id,
    /// including the permissions nodes for a specific target channel
    /// </summary>
    public async Task<List<PlanetRole>> GetRolesAndNodesAsync(long memberId, long targetId, PermissionsTargetType type) =>
        await _db.PlanetRoleMembers.Where(x => x.MemberId == memberId)
            .Include(x => x.Role)
            .ThenInclude(r => r.PermissionNodes.Where(n => n.TargetId == targetId && n.TargetType == type))
            .OrderBy(x => x.Role.Position)
            .Select(x => x.Role)
            .ToListAsync();

    /// <summary>
    /// Returns the primary (top) role for the given PlanetMember id
    /// </summary>
    public async Task<PlanetRole> GetPrimaryRoleAsync(long id) =>
        await _db.PlanetRoleMembers.Where(x => x.MemberId == id)
            .Include(x => x.Role)
            .OrderBy(x => x.Role.Position)
            .Select(x => x.Role)
            .FirstOrDefaultAsync();

    /// <summary>
    /// Returns if the member has the given planet permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetMember member, PlanetPermission perm) =>
        await _permissionsService.HasPermissionAsync(member, perm);
}