using Valour.Server.Database;
using Valour.Server.Workers;
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
    private readonly PushNotificationWorker _pushNotificationWorker;

    public PlanetRoleService(
        ValourDb db,
        ILogger<PlanetRoleService> logger,
        CoreHubService coreHub,
        HostedPlanetService hostedService, 
        PlanetPermissionService permissionService, 
        PushNotificationWorker pushNotificationWorker)
    {
        _db = db;
        _logger = logger;
        _coreHub = coreHub;
        _hostedService = hostedService;
        _permissionService = permissionService;
        _pushNotificationWorker = pushNotificationWorker;
    }

    /// <summary>
    /// Returns the planet role with the given id
    /// </summary>
    public async ValueTask<PlanetRole> GetAsync(long planetId, long roleId)
    {
        var hosted = await _hostedService.GetRequiredAsync(planetId);
        return hosted.GetRoleById(roleId);
    }

    private static readonly Regex _hexColorRegex = new Regex("^#([a-fA-F0-9]{6}|[a-fA-F0-9]{3})$");

    public async Task<TaskResult<PlanetRole>> CreateAsync(PlanetRole role)
    {
        var hostedPlanet = await _hostedService.GetRequiredAsync(role.PlanetId);
        
        if (string.IsNullOrWhiteSpace(role.Color))
            role.Color = "#ffffff";

        if (!_hexColorRegex.IsMatch(role.Color))
            return new TaskResult<PlanetRole>(false, "Invalid hex color");

        role.Id = IdManager.Generate();

        // The index should be the next free index
        var roles = await _db.PlanetRoles.Where(x => x.PlanetId == role.PlanetId)
            .OrderBy(x => x.FlagBitIndex)
            .ToListAsync();

        // Get the default role and calculate new role position based on max existing position
        // (not count, since positions can be reordered)
        var defaultRole = roles.FirstOrDefault(x => x.IsDefault);
        var maxNonDefaultPosition = roles.Where(x => !x.IsDefault).Max(x => (uint?)x.Position) ?? 0;

        // New role goes just before default (or at 0 if no other non-default roles exist)
        role.Position = roles.Any(x => !x.IsDefault) ? maxNonDefaultPosition + 1 : 0;

        // Bump default role to stay last if needed
        if (defaultRole != null && defaultRole.Position <= role.Position)
        {
            defaultRole.Position = role.Position + 1;
            _db.PlanetRoles.Update(defaultRole);
        }

        // Below, we find the first free space in the role indices. There may be a gap due to
        // a deletion, in which case we want to use that space first.
        
        var indiceArr = new int[256];
        foreach (var r in roles)
        {
            indiceArr[r.FlagBitIndex] = 1;
        }
        
        // Walk array and look for first 0
        for (int i = 0; i < indiceArr.Length; i++)
        {
            if (indiceArr[i] == 0)
            {
                role.FlagBitIndex = i;
                break;
            }
        }
        
        // Track if we need to update the default role
        var defaultRoleUpdated = defaultRole != null && defaultRole.Position == role.Position + 1;

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

        // If we bumped the default role, update cache and notify clients
        if (defaultRoleUpdated)
        {
            var defaultRoleModel = defaultRole.ToModel();
            hostedPlanet.UpsertRole(defaultRoleModel);
            _coreHub.NotifyPlanetItemChange(defaultRoleModel);
        }

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
        var role = hostedPlanet.GetRoleById(roleId);
        if (role is null) return new(false, "Role not found in hosted planet");
            
        if (role.IsDefault)
            return new (false, "Cannot delete default roles");

        await using var trans = await _db.Database.BeginTransactionAsync();
        
        try
        {
            // Remove role nodes
            
            await _db.PermissionsNodes.Where(x => x.RoleId == roleId)
                .ExecuteDeleteAsync();
            
            // Update role membership flags

            var flagChanges = await _db.PlanetMembers.WithRoleByLocalIndex(role.PlanetId, role.FlagBitIndex)
                .BulkSetRoleFlag(role.PlanetId, role.FlagBitIndex, false);
            
            _logger.LogInformation("Role flag changes for deletion: {Changes}", flagChanges);

            var deleted = await _db.PlanetRoles.Where(x => x.Id == roleId)
                .ExecuteDeleteAsync();

            if (deleted == 0)
            {
                _logger.LogError("Executed delete on role but no rows affected: {RoleId}", roleId);
            }
            
            await trans.CommitAsync();

        }
        catch (Exception e)
        {
            await trans.RollbackAsync();
            _logger.LogError("DB Error removing role: {Error}", e.Message);
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