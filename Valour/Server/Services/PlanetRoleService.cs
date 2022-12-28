
using Valour.Server.Database;
using Valour.Server.Database.Items.Planets.Members;

namespace Valour.Server.Services;

public class PlanetRoleService
{
    private readonly ValourDB _db;
    
    public PlanetRoleService(ValourDB db)
    {
        _db = db;
    }
    
    public ValueTask<PlanetRole> GetAsync(long id) =>
        _db.PlanetRoles.FindAsync(id);
    
    public ICollection<PermissionsNode> GetNodes(ValourDB db)
    {
        PermissionNodes ??= db.PermissionsNodes.Where(x => x.RoleId == Id).ToList();
        return PermissionNodes;
    }

    public ICollection<PermissionsNode> GetChannelNodes(ValourDB db)
    {
        PermissionNodes ??= db.PermissionsNodes.Where(x => x.RoleId == Id).ToList();
        return PermissionNodes.Where(x => x.TargetType == PermissionsTargetType.PlanetChatChannel &&
                                          x.RoleId == Id).ToList();
    }

    public ICollection<PermissionsNode> GetCategoryNodes(ValourDB db)
    {
        PermissionNodes ??= db.PermissionsNodes.Where(x => x.RoleId == Id).ToList();
        return PermissionNodes.Where(x => x.TargetType == PermissionsTargetType.PlanetCategoryChannel &&
                                          x.RoleId == Id).ToList();
    }

    public async Task<PermissionsNode> GetChatChannelNodeAsync(PlanetChatChannel channel, ValourDB db) =>
        await db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == channel.Id &&
                                                           x.TargetType == PermissionsTargetType.PlanetChatChannel &&
                                                           x.RoleId == Id);

    public async Task<PermissionsNode> GetChatChannelNodeAsync(PlanetCategory category, ValourDB db) =>
        await db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == category.Id &&
                                                           x.TargetType == PermissionsTargetType.PlanetChatChannel &&
                                                           x.RoleId == Id);

    public async Task<PermissionsNode> GetCategoryNodeAsync(PlanetCategory category, ValourDB db) =>
        await db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == category.Id &&
                                                           x.TargetType == PermissionsTargetType.PlanetCategoryChannel &&
                                                           x.RoleId == Id);

    public async Task<PermissionState> GetPermissionStateAsync(Permission permission, PlanetChatChannel channel, ValourDB db) =>
        await GetPermissionStateAsync(permission, channel.Id, db);

    public async Task<PermissionState> GetPermissionStateAsync(Permission permission, long channelId, ValourDB db) =>
        (await db.PermissionsNodes.FirstOrDefaultAsync(x => x.RoleId == Id && x.TargetId == channelId)).GetPermissionState(permission);

    public async Task DeleteAsync(ValourDB db)
    {
        // Remove all members
        var members = db.PlanetRoleMembers.Where(x => x.RoleId == Id);
        db.PlanetRoleMembers.RemoveRange(members);

        // Remove role nodes
        var nodes = GetNodes(db);

        db.PermissionsNodes.RemoveRange(nodes);

        // Remove self
        db.PlanetRoles.Remove(this);
    }
}