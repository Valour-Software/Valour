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

    public async Task<TaskResult<PlanetRole>> CreateAsync(PlanetRole role)
    {
        role.Position = await _db.PlanetRoles.CountAsync(x => x.PlanetId == role.PlanetId);
        role.Id = IdManager.Generate();

        try
        {
            await _db.AddAsync(role);
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(true, e.Message);
        }

        _coreHub.NotifyPlanetItemChange(role);

        return new(true, "Success", role);
    }

    public async Task<TaskResult<PlanetRole>> PutAsync(PlanetRole updatedRole)
    {
        var oldRole = await _db.PlanetRoles.FindAsync(updatedRole.Id);
        if (oldRole is null) return new(false, $"PlanetRole not found");

        if (updatedRole.PlanetId != oldRole.PlanetId)
            return new(false, "You cannot change what planet.");

        if (updatedRole.Position != oldRole.Position)
            return new(false, "Position cannot be changed directly.");
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

        return new(true, "Success");
    }

    public async Task<List<PermissionsNode>> GetNodesAsync(PlanetRole role) =>
        await _db.PermissionsNodes.Where(x => x.RoleId == role.Id).Select(x => x.ToModel()).ToListAsync();

    public async Task<List<PermissionsNode>> GetChannelNodesAsync(PlanetRole role) =>
        await _db.PermissionsNodes.Where(x => x.TargetType == PermissionsTargetType.PlanetChatChannel &&
                                          x.RoleId == role.Id).Select(x => x.ToModel()).ToListAsync();

    public async Task<List<PermissionsNode>> GetCategoryNodesAsync(PlanetRole role) =>
        await _db.PermissionsNodes.Where(x => x.TargetType == PermissionsTargetType.PlanetCategoryChannel &&
                                          x.RoleId == role.Id).Select(x => x.ToModel()).ToListAsync();

    public async Task<PermissionsNode> GetChatChannelNodeAsync(PlanetChatChannel channel, PlanetRole role) =>
        (await _db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == channel.Id &&
                                                           x.TargetType == PermissionsTargetType.PlanetChatChannel &&
                                                           x.RoleId == role.Id)).ToModel();

    public async Task<PermissionsNode> GetChatChannelNodeAsync(PlanetCategory category, PlanetRole role) =>
        (await _db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == category.Id &&
                                                           x.TargetType == PermissionsTargetType.PlanetChatChannel &&
                                                           x.RoleId == role.Id)).ToModel();

    public async Task<PermissionsNode> GetCategoryNodeAsync(PlanetCategory category, PlanetRole role) =>
        (await _db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == category.Id &&
                                                           x.TargetType == PermissionsTargetType.PlanetCategoryChannel &&
                                                           x.RoleId == role.Id)).ToModel();

    public async Task<PermissionState> GetPermissionStateAsync(Permission permission, long channelId, long roleId) =>
        (await _db.PermissionsNodes.Where(x => x.RoleId == roleId && x.TargetId == channelId).Select(x => x.ToModel()).FirstOrDefaultAsync())
            .GetPermissionState(permission);

    public async Task<TaskResult> DeleteAsync(PlanetRole role)
    {
        try
        {
            // Remove all members
            var members = _db.PlanetRoleMembers.Where(x => x.RoleId == role.Id);
            _db.PlanetRoleMembers.RemoveRange(members);

            // Remove role nodes
            var nodes = await GetNodesAsync(role);

            _db.PermissionsNodes.RemoveRange(nodes.Select(x => x.ToDatabase()));

            // Remove the role
            _db.PlanetRoles.Remove(await _db.PlanetRoles.FindAsync(role.Id));

            await _db.SaveChangesAsync();
            _coreHub.NotifyPlanetItemDelete(role);
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        return new(true, "Success");
    }
}