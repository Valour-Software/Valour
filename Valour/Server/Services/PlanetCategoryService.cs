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

    /// <summary>
    /// Returns the children of the category with the given id
    /// </summary>
    public async Task<List<PlanetChannel>> GetChildrenAsync(long id) =>
        await _db.PlanetChannels.Where(x => x.ParentId == id)
            .OrderBy(x => x.Position)
            .Select(x => x.ToModel()).ToListAsync();
    
    /// <summary>
    /// Returns the number of children in the category with the given id
    /// </summary>
    public async Task<int> GetChildCountAsync(long id) =>
        await _db.PlanetChannels.Where(x => x.ParentId == id)
            .CountAsync();

    /// <summary>
    /// Returns if a category is the last category in a planet
    /// </summary>
    public async Task<bool> IsLastCategory(PlanetCategory category) =>
        (await _db.PlanetCategories.Where(x => x.PlanetId == category.PlanetId).CountAsync()) < 2;

    /// <summary>
    /// Returns the ids of the children of the category with the given id
    /// </summary>
    public async Task<List<long>> GetChildrenIdsAsync(long id) =>
        await _db.PlanetChannels.Where(x => x.ParentId == id)
            .OrderBy(x => x.Position)
            .Select(x => x.Id).ToListAsync();

    /// <summary>
    /// Returns the children of the category with the given id
    /// </summary>
    public async Task<List<PlanetChannel>> GetChildrenAsync(PlanetCategory category) =>
        await GetChildrenAsync(category.Id);

    /// <summary>
    /// Returns if a given member has a channel permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetCategory channel, PlanetMember member, CategoryPermission permission) =>
        await _planetMemberService.HasPermissionAsync(member, channel, permission);
    
    /// <summary>
    /// Returns if a given member has a channel permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetCategory channel, PlanetMember member, ChatChannelPermission permission) =>
        await _planetMemberService.HasPermissionAsync(member, channel, permission);

    /// <summary>
    /// Deletes the given category
    /// </summary>
    public async Task DeleteAsync(PlanetCategory category)
    {
        var dbcategory = await _db.PlanetCategories.FindAsync(category.Id);
        dbcategory.IsDeleted = true;
        await _db.SaveChangesAsync();

        _coreHub.NotifyPlanetItemDelete(category);
    }
    
    /// <summary>
    /// Common basic validation for categories
    /// </summary>
    private async Task<TaskResult> ValidateBasic(PlanetCategory category)
    {
        var nameValid = ValidateName(category.Name);
        if (!nameValid.Success)
            return new TaskResult(false, nameValid.Message);

        var descValid = ValidateDescription(category.Description);
        if (!descValid.Success)
            return new TaskResult(false, descValid.Message);

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Validates that a given name is allowable
    /// </summary>
    public static TaskResult ValidateName(string name)
    {
        if (name.Length > 32)
        {
            return new TaskResult(false, "Planet names must be 32 characters or less.");
        }
        
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Validates that a given description is allowable
    /// </summary>
    public static TaskResult ValidateDescription(string desc)
    {
        if (desc.Length > 500)
        {
            return new TaskResult(false, "Planet descriptions must be 500 characters or less.");
        }

        return TaskResult.SuccessResult;
    }
    
    public async Task<TaskResult> InsertChildAsync(long categoryId, long insertId, int position = -1)
    {
        var category = await _db.PlanetCategories.FindAsync(categoryId);
        if (category is null)
            return new TaskResult(false, "Category not found.");

        var insert = await _db.PlanetChannels.FindAsync(insertId);
        if (insert is null)
            return new TaskResult(false, "Child to insert not found.");
        
        var children = await _db.PlanetChannels
            .Where(x => x.ParentId == category.Id)
            .OrderBy(x => x.Position)
            .ToListAsync();

        // If unspecified or too high, set to next position
        if (position < 0 || position > children.Count)
        {
            position = children.Count + 1;
        }
        
        var oldCategoryId = insert.ParentId;
        List<long> oldCategoryOrder = null;

        if (oldCategoryId is not null)
        {
            var oldCategory = await _db.PlanetCategories.FindAsync(insert.ParentId);
            if (oldCategory is null)
                return new TaskResult(false, "Error getting old parent category.");

            var oldCategoryChildren = await _db.PlanetChannels
                .Where(x => x.ParentId == oldCategory.Id)
                .OrderBy(x => x.Position)
                .ToListAsync();

            // Remove from old category
            oldCategoryChildren.RemoveAll(x => x.Id == insertId);

            oldCategoryOrder = new();
            
            // Update all positions
            var opos = 0;
            foreach (var child in oldCategoryChildren)
            {
                child.Position = opos;
                oldCategoryOrder.Add(child.Id);
                opos++;
            }
        }

        insert.ParentId = category.Id;
        insert.PlanetId = category.PlanetId;
        insert.Position = position;
        
        children.Insert(position, insert);


        // Positions for new category
        List<long> newCategoryOrder = new();
        
        // Update all positions
        var pos = 0;
        foreach (var child in children)
        {
            child.Position = pos;
            newCategoryOrder.Add(child.Id);
            pos++;
        }
        
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            return new TaskResult(false, "Error saving changes. Please try again later.");
        }
        
        // Fire off events for both modified categories (if applicable)
        
        // New parent
        _coreHub.NotifyCategoryOrderChange(new CategoryOrderEvent()
        {
            PlanetId = category.PlanetId,
            CategoryId = categoryId,
            Order = newCategoryOrder
        });

        if (oldCategoryId is not null)
        {
            _coreHub.NotifyCategoryOrderChange(new CategoryOrderEvent()
            {
                PlanetId = category.PlanetId,
                CategoryId = oldCategoryId.Value,
                Order = oldCategoryOrder,
            });
        }

        return new(true, "Success");
    }

    public async Task<TaskResult> SetChildOrderAsync(PlanetCategory category, long[] order)
    {
        var totalChildren = await _db.PlanetChannels.CountAsync(x => x.ParentId == category.Id);

        if (totalChildren != order.Length)
            return new(false, "Your order does not contain all the children.");

        // Use transaction so we can stop at any failure
        await using var tran = await _db.Database.BeginTransactionAsync();

        List<Valour.Database.PlanetChannel> children = new();

        try
        {
            var pos = 0;
            foreach (var childId in order)
            {
                var child = await _db.PlanetChannels.FindAsync(childId);
                if (child is null)
                    return new(false, $"Child with id {childId} does not exist!");

                if (child.ParentId != category.Id)
                    return new(false, $"Category {childId} is not a child of {category.Id}.");

                child.Position = pos;

                children.Add(child);

                pos++;
            }

            await _db.SaveChangesAsync();
            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }
        
        _coreHub.NotifyCategoryOrderChange(new ()
        {
            PlanetId = category.PlanetId,
            CategoryId = category.Id,
            Order = order.ToList()
        });

        return new(true, "Success");
    }

    /// <summary>
    /// Validates the parent and position of this category
    /// </summary>
    public async Task<TaskResult> ValidateParentAndPosition(PlanetCategory category)
    {
        if (category.ParentId != null)
        {
            var parent = await _db.PlanetCategories.FindAsync(category.ParentId);
            if (parent == null) return new TaskResult(false, "Could not find parent");
            if (parent.PlanetId != category.PlanetId) return new TaskResult(false, "Parent category belongs to a different planet");
            if (parent.Id == category.Id) return new TaskResult(false, "Cannot be own parent");

            // Automatically determine position in this case
            if (category.Position < 0)
            {
                category.Position = (ushort)await _db.PlanetChannels.CountAsync(x => x.ParentId == category.ParentId);
            }
            else
            {
                // Ensure position is not already taken
                if (!await HasUniquePosition(category))
                    return new TaskResult(false, "The position is already taken.");
            }

            // Ensure this category does not contain itself
            var loop_parent = parent;

            while (loop_parent.ParentId != null)
            {
                if (loop_parent.ParentId == category.Id)
                {
                    return new TaskResult(false, "Cannot create parent loop.");
                }

                loop_parent = await _db.PlanetCategories.FindAsync(loop_parent.ParentId);
            }
        }
        else
        {
            if (category.Position < 0)
            {
                category.Position = (ushort)await _db.PlanetChannels.CountAsync(x => x.PlanetId == category.PlanetId && x.ParentId == null);
            }
            else
            {
                // Ensure position is not already taken
                if (!await HasUniquePosition(category))
                    return new TaskResult(false, "The position is already taken.");
            }
        }

        return TaskResult.SuccessResult;
    }

    public async Task<bool> HasUniquePosition(PlanetChannel channel) =>
        // Ensure position is not already taken
        !await _db.PlanetChannels.AnyAsync(x => x.ParentId == channel.ParentId && // Same parent
                                                x.Position == channel.Position && // Same position
                                                x.Id != channel.Id); // Not self
}