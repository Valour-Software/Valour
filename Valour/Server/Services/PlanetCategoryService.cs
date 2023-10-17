using IdGen;
using StackExchange.Redis;
using Valour.Server.Database;
using Valour.Server.Requests;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace Valour.Server.Services;

public class PlanetCategoryService
{
    private readonly ValourDB _db;
    private readonly PlanetMemberService _planetMemberService;
    private readonly CoreHubService _coreHub;
    private readonly ILogger<PlanetCategoryService> _logger;

    public PlanetCategoryService(
        ValourDB db, 
        PlanetMemberService planetMemberService,
        CoreHubService coreHub,
        ILogger<PlanetCategoryService> logger)
    {
        _db = db;
        _planetMemberService = planetMemberService;
        _coreHub = coreHub;
        _logger = logger;
    }

    /// <summary>
    /// Returns the category with the given id
    /// </summary>
    public async ValueTask<PlanetCategory> GetAsync(long id) =>
        (await _db.PlanetCategories.FindAsync(id)).ToModel();

    /// <summary>
    /// Creates the given planet category
    /// </summary>
    public async Task<TaskResult<PlanetCategory>> CreateAsync(PlanetCategory category)
    {
        var baseValid = await ValidateBasic(category);
        if (!baseValid.Success)
            return new(false, baseValid.Message);

        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            await _db.PlanetCategories.AddAsync(category.ToDatabase());
            await _db.SaveChangesAsync();

            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create planet category");
            await tran.RollbackAsync();
            return new(false, "Failed to create category");
        }

        _coreHub.NotifyPlanetItemChange(category);

        return new(true, "PlanetCategory created successfully", category);
    }

    /// <summary>
    /// Creates the given planet category (detailed)
    /// </summary>
    public async Task<TaskResult<PlanetCategory>> CreateDetailedAsync(CreatePlanetCategoryChannelRequest request, PlanetMember member)
    {
        var category = request.Category;
        var baseValid = await ValidateBasic(category);
        if (!baseValid.Success)
            return new(false, baseValid.Message);

        List<PermissionsNode> nodes = new();

        // Create nodes
        foreach (var nodeReq in request.Nodes)
        {
            var node = nodeReq;
            node.TargetId = category.Id;
            node.PlanetId = category.PlanetId;

            var role = await _db.PlanetRoles.FindAsync(node.RoleId);
            if (role is null)
                return new(false, "Role not found.");
            
            if (role.GetAuthority() > await _planetMemberService.GetAuthorityAsync(member))
                return new(false, "A permission node's role has higher authority than you.");

            node.Id = IdManager.Generate();

            nodes.Add(node);
        }

        var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            await _db.PlanetCategories.AddAsync(category.ToDatabase());
            await _db.SaveChangesAsync();

            await _db.PermissionsNodes.AddRangeAsync(nodes.Select(x => x.ToDatabase()));
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        await tran.CommitAsync();

        _coreHub.NotifyPlanetItemChange(category);

        return new(true, "Success", category);
    }

    public async Task<TaskResult<PlanetCategory>> UpdateAsync(PlanetCategory updated)
    {
        var old = await _db.PlanetCategories.FindAsync(updated.Id);
        if (old is null) return new(false, $"PlanetCategory not found");

        // Validation
        if (old.Id != updated.Id)
            return new(false, "Cannot change Id.");

        if (old.PlanetId != updated.PlanetId)
            return new(false, "Cannot change PlanetId.");

        var baseValid = await ValidateBasic(updated);
        if (!baseValid.Success)
            return new(false, baseValid.Message);

        if (updated.ParentId != old.ParentId ||
            updated.Position != old.Position)
        {
            var positionValid = await ValidateParentAndPosition(updated);
            if (!positionValid.Success)
                return new (false, positionValid.Message);
        }

        // Update
        try
        {
            _db.Entry(old).CurrentValues.SetValues(updated);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemChange(updated);

        return new(true, "Success", updated);
    }
    
}