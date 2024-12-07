using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class PlanetRoleService
{
    private readonly ValourDb _db;
    private readonly ILogger<PlanetRoleService> _logger;
    private readonly CoreHubService _coreHub;
    private readonly ChannelAccessService _accessService;

    public PlanetRoleService(
        ValourDb db, 
        ILogger<PlanetRoleService> logger, 
        CoreHubService coreHub,
        ChannelAccessService accessService)
    {
        _db = db;
        _logger = logger;
        _coreHub = coreHub;
        _accessService = accessService;
    }

    /// <summary>
    /// Returns the planert role with the given id
    /// </summary>
    public async ValueTask<PlanetRole> GetAsync(long id) =>
        (await _db.PlanetRoles.FindAsync(id)).ToModel();

    private static readonly Regex _hexColorRegex = new Regex("^#([a-fA-F0-9]{6}|[a-fA-F0-9]{3})$");
    
    public async Task<TaskResult<PlanetRole>> CreateAsync(PlanetRole role)
    {
        if (string.IsNullOrWhiteSpace(role.Color))
            role.Color = "#ffffff";
        
        if (!_hexColorRegex.IsMatch(role.Color))
            return new TaskResult<PlanetRole>(false, "Invalid hex color");

        role.Position = (uint)await _db.PlanetRoles.CountAsync(x => x.PlanetId == role.PlanetId);
        role.Id = IdManager.Generate();

        try
        {
            await _db.AddAsync(role.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemChange(role);

        return new(true, "Success", role);
    }

    public async Task<TaskResult<PlanetRole>> UpdateAsync(PlanetRole updatedRole)
    {
        var oldRole = await _db.PlanetRoles.FindAsync(updatedRole.Id);
        if (oldRole is null) return new(false, $"PlanetRole not found");

        if (updatedRole.PlanetId != oldRole.PlanetId)
            return new(false, "You cannot change the planet.");

        if (updatedRole.Position != oldRole.Position)
            return new(false, "Position cannot be changed directly.");

        if (updatedRole.IsDefault != oldRole.IsDefault)
            return new TaskResult<PlanetRole>(false, "Cannot change default status of role.");

        if (string.IsNullOrWhiteSpace(updatedRole.Color))
            updatedRole.Color = "#ffffff";
        
        if (updatedRole.Color != oldRole.Color)
        {
            if (!_hexColorRegex.IsMatch(updatedRole.Color))
                return new TaskResult<PlanetRole>(false, "Invalid hex color");
        }
        
        var trans = await _db.Database.BeginTransactionAsync();

        try
        {
            // If any permissions or admin state changed, we need to recalculate access
            var updateAccess = (updatedRole.IsAdmin != oldRole.IsAdmin ||
                                updatedRole.Permissions != oldRole.Permissions ||
                                updatedRole.CategoryPermissions != oldRole.CategoryPermissions ||
                                updatedRole.ChatPermissions != oldRole.ChatPermissions ||
                                updatedRole.VoicePermissions != oldRole.VoicePermissions);
            
            _db.Entry(oldRole).CurrentValues.SetValues(updatedRole);
            await _db.SaveChangesAsync();
            
            // Check if any permissions were changed
            if (updateAccess)
            {
                // Recalculate access
                await _accessService.UpdateAllChannelAccessForMembersInRole(oldRole.Id);
                await _db.SaveChangesAsync();
            }

            await trans.CommitAsync();
        }
        catch (System.Exception e)
        {
            await trans.RollbackAsync();
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemChange(updatedRole);

        return new(true, "Success", updatedRole);
    }

    public async Task<List<PermissionsNode>> GetNodesAsync(PlanetRole role) =>
        await _db.PermissionsNodes.Where(x => x.RoleId == role.Id).Select(x => x.ToModel()).ToListAsync();

    public async Task<List<PermissionsNode>> GetChannelNodesAsync(PlanetRole role) =>
        await _db.PermissionsNodes.Where(x => x.TargetType == ChannelTypeEnum.PlanetChat &&
                                          x.RoleId == role.Id)
            .Select(x => x.ToModel())
            .ToListAsync();

    public async Task<List<PermissionsNode>> GetCategoryNodesAsync(PlanetRole role) =>
        await _db.PermissionsNodes.Where(x => x.TargetType == ChannelTypeEnum.PlanetCategory &&
                                          x.RoleId == role.Id)
            .Select(x => x.ToModel())
            .ToListAsync();

    public async Task<PermissionsNode> GetChatChannelNodeAsync(Channel channel, PlanetRole role) =>
        (await _db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == channel.Id &&
                                                           x.TargetType == ChannelTypeEnum.PlanetChat &&
                                                           x.RoleId == role.Id)).ToModel();

    public async Task<PermissionsNode> GetCategoryNodeAsync(Channel category, PlanetRole role) =>
        (await _db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == category.Id &&
                                                           x.TargetType == ChannelTypeEnum.PlanetCategory &&
                                                           x.RoleId == role.Id)).ToModel();

    public async Task<PermissionState> GetPermissionStateAsync(Permission permission, long channelId, long roleId) =>
        (await _db.PermissionsNodes.Where(x => x.RoleId == roleId && x.TargetId == channelId).Select(x => x.ToModel()).FirstOrDefaultAsync())
            .GetPermissionState(permission); 

    public async Task<TaskResult> DeleteAsync(PlanetRole role)
    {
        var dbRole = await _db.PlanetRoles.FindAsync(role.Id);
        if (dbRole is null) return new(false, "Role not found");
            
        if (dbRole.IsDefault)
            return new (false, "Cannot delete default roles");

        await using var trans = await _db.Database.BeginTransactionAsync();
        
        try
        {
            // This order is important. We recalculate access after removing permission nodes
            // but BEFORE the role itself is removed. This is because the procedure to
            // recalculate access needs the role members to be present.
            
            // We also set all the permissions to FALSE in the role so it acts like it doesn't exist
            
            // Set all permissions to false
            dbRole.Permissions = 0;
            dbRole.ChatPermissions = 0;
            dbRole.CategoryPermissions = 0;
            dbRole.VoicePermissions = 0;
            
            await _db.SaveChangesAsync();
            
            // Remove role nodes
            var nodes = _db.PermissionsNodes.Where(x => x.RoleId == role.Id);
            _db.PermissionsNodes.RemoveRange(nodes);

            await _db.SaveChangesAsync();
            
            // Recalculate access
            await _accessService.UpdateAllChannelAccessForMembersInRole(role.Id);
            await _db.SaveChangesAsync();
            
            // Remove all members
            var members = _db.PlanetRoleMembers.Where(x => x.RoleId == role.Id);
            _db.PlanetRoleMembers.RemoveRange(members);
            await _db.SaveChangesAsync();

            // Remove the role
            _db.PlanetRoles.Remove(dbRole);

            await _db.SaveChangesAsync();
            await trans.CommitAsync();

        }
        catch (System.Exception e)
        {
            await trans.RollbackAsync();
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }
        
        _coreHub.NotifyPlanetItemDelete(role);

        return new(true, "Success");
    }
}