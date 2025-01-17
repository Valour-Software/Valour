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
    private readonly HostedPlanetService _hostedService;
    private readonly PlanetPermissionService _permissionService;

    public PlanetRoleService(
        ValourDb db,
        ILogger<PlanetRoleService> logger,
        CoreHubService coreHub,
        HostedPlanetService hostedService, 
        PlanetPermissionService permissionService)
    {
        _db = db;
        _logger = logger;
        _coreHub = coreHub;
        _hostedService = hostedService;
        _permissionService = permissionService;
    }

    /// <summary>
    /// Returns the planet role with the given id
    /// </summary>
    public async ValueTask<PlanetRole> GetAsync(long planetId, long roleId)
    {
        var hosted = await _hostedService.GetRequiredAsync(planetId);
        return hosted.GetRole(roleId);
    }

    private static readonly Regex _hexColorRegex = new Regex("^#([a-fA-F0-9]{6}|[a-fA-F0-9]{3})$");

    public async Task<TaskResult<PlanetRole>> CreateAsync(PlanetRole role)
    {
        var hostedPlanet = await _hostedService.GetRequiredAsync(role.PlanetId);
        
        if (string.IsNullOrWhiteSpace(role.Color))
            role.Color = "#ffffff";

        if (!_hexColorRegex.IsMatch(role.Color))
            return new TaskResult<PlanetRole>(false, "Invalid hex color");

        role.Position = (uint)await _db.PlanetRoles.CountAsync(x => x.PlanetId == role.PlanetId && !x.IsDefault);
        role.Id = IdManager.Generate();
        
        try
        {
            await _db.AddAsync(role.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError("Error saving role to database: {Error}", e.Message);
            return new(false, e.Message);
        }
        
        hostedPlanet.UpsertRole(role);

        _coreHub.NotifyPlanetItemChange(role);

        return new(true, "Success", role);
    }

    public async Task<TaskResult<PlanetRole>> UpdateAsync(PlanetRole updatedRole)
    {
        var hostedPlanet = await _hostedService.GetRequiredAsync(updatedRole.PlanetId);
        
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

        // If any permissions or admin state changed, we need to recalculate permissions
        var updatePerms = (updatedRole.IsAdmin != oldRole.IsAdmin ||
                           updatedRole.Permissions != oldRole.Permissions ||
                           updatedRole.CategoryPermissions != oldRole.CategoryPermissions ||
                           updatedRole.ChatPermissions != oldRole.ChatPermissions ||
                           updatedRole.VoicePermissions != oldRole.VoicePermissions);
        
        try
        {
            _db.Entry(oldRole).CurrentValues.SetValues(updatedRole);
            await _db.SaveChangesAsync();

            await trans.CommitAsync();
        }
        catch (System.Exception e)
        {
            await trans.RollbackAsync();
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }
        
        // Update in cache
        hostedPlanet.UpsertRole(updatedRole);
        
        // If any permissions were changed
        if (updatePerms)
        {
            await _permissionService.HandleRoleChange(updatedRole);
        }

        _coreHub.NotifyPlanetItemChange(updatedRole);

        return new(true, "Success", updatedRole);
    }

    public async Task<List<PermissionsNode>> GetNodesAsync(long roleId) =>
        await _db.PermissionsNodes.Where(x => x.RoleId == roleId).Select(x => x.ToModel()).ToListAsync();

    public Task<TaskResult> DeleteAsync(PlanetRole role) =>
        DeleteAsync(role.PlanetId, role.Id);

    public async Task<TaskResult> DeleteAsync(long planetId, long roleId)
    {
        var hostedPlanet = await _hostedService.GetRequiredAsync(planetId);
        var role = hostedPlanet.GetRole(roleId);
        if (role is null) return new(false, "Role not found in hosted planet");
        
        var dbRole = await _db.PlanetRoles.FindAsync(roleId);
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
            var nodes = _db.PermissionsNodes.Where(x => x.RoleId == roleId);
            _db.PermissionsNodes.RemoveRange(nodes);

            await _db.SaveChangesAsync();
            
            // Remove all members
            var members = _db.PlanetRoleMembers.Where(x => x.RoleId == roleId);
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
        
        // Update permissions related to the role
        await _permissionService.HandleRoleChange(role);
        
        // Remove from hosted cache
        hostedPlanet.RemoveRole(role.Id);
        
        _coreHub.NotifyPlanetItemDelete(role);

        return new(true, "Success");
    }
}