using IdGen;
using StackExchange.Redis;
using System.Security.Cryptography;
using Valour.Database.Context;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class PlanetRoleService
{
    private readonly ValourDB _db;
    private readonly ILogger<PlanetRoleService> _logger;
    private readonly CoreHubService _coreHub;

    public PlanetRoleService(
        ValourDB db, 
        ILogger<PlanetRoleService> logger, 
        CoreHubService coreHub)
    {
        _db = db;
        _logger = logger;
        _coreHub = coreHub;
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

        role.Position = await _db.PlanetRoles.CountAsync(x => x.PlanetId == role.PlanetId);
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

        try
        {
            _db.Entry(oldRole).CurrentValues.SetValues(updatedRole);
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemChange(updatedRole);

        return new(true, "Success", updatedRole);
    }

    public async Task<List<PermissionsNode>> GetNodesAsync(PlanetRole role) =>
        await _db.PermissionsNodes.Where(x => x.RoleId == role.Id).Select(x => x.ToModel()).ToListAsync();

    public async Task<List<PermissionsNode>> GetChannelNodesAsync(PlanetRole role) =>
        await _db.PermissionsNodes.Where(x => x.TargetType == ChannelType.PlanetChatChannel &&
                                          x.RoleId == role.Id).Select(x => x.ToModel()).ToListAsync();

    public async Task<List<PermissionsNode>> GetCategoryNodesAsync(PlanetRole role) =>
        await _db.PermissionsNodes.Where(x => x.TargetType == ChannelType.PlanetCategoryChannel &&
                                          x.RoleId == role.Id).Select(x => x.ToModel()).ToListAsync();

    public async Task<PermissionsNode> GetChatChannelNodeAsync(PlanetChatChannel channel, PlanetRole role) =>
        (await _db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == channel.Id &&
                                                           x.TargetType == ChannelType.PlanetChatChannel &&
                                                           x.RoleId == role.Id)).ToModel();

    public async Task<PermissionsNode> GetChatChannelNodeAsync(PlanetCategory category, PlanetRole role) =>
        (await _db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == category.Id &&
                                                           x.TargetType == ChannelType.PlanetChatChannel &&
                                                           x.RoleId == role.Id)).ToModel();

    public async Task<PermissionsNode> GetCategoryNodeAsync(PlanetCategory category, PlanetRole role) =>
        (await _db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == category.Id &&
                                                           x.TargetType == ChannelType.PlanetCategoryChannel &&
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
            // Remove all members
            var members = _db.PlanetRoleMembers.Where(x => x.RoleId == role.Id);
            _db.PlanetRoleMembers.RemoveRange(members);

            // Remove role nodes
            var nodes = _db.PermissionsNodes.Where(x => x.RoleId == role.Id);
            _db.PermissionsNodes.RemoveRange(nodes);

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