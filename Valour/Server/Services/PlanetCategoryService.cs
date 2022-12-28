using Valour.Server.Database;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Shared.Authorization;

namespace Valour.Server.Services;

public class PlanetCategoryService
{
    private readonly ValourDB _db;
    private readonly PlanetService _planetService;
    private readonly PlanetMemberService _planetMemberService;
    private readonly PermissionsService _permissionsService;
    
    public PlanetCategoryService(
        ValourDB db, 
        PlanetService planetService, 
        PlanetMemberService planetMemberService,
        PermissionsService permissionsService)
    {
        _db = db;
        _planetService = planetService;
        _planetMemberService = planetMemberService;
        _permissionsService = permissionsService;
    }

    /// <summary>
    /// Returns the category with the given id
    /// </summary>
    public async ValueTask<PlanetCategoryChannel> GetAsync(long id) =>
        await _db.PlanetCategoryChannels.FindAsync(id);

    /// <summary>
    /// Returns the children of the category with the given id
    /// </summary>
    public async Task<List<PlanetChannel>> GetChildrenAsync(long id) =>
        await _db.PlanetChannels.Where(x => x.Id == id).ToListAsync();

    /// <summary>
    /// Returns the children of the category with the given id
    /// </summary>
    public async Task<List<PlanetChannel>> GetChildrenAsync(PlanetCategoryChannel category) =>
        await GetChildrenAsync(category.Id);

    /// <summary>
    /// Returns if a given member has a channel permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetCategoryChannel channel, PlanetMember member, CategoryPermission permission) =>
        await _permissionsService.HasPermissionAsync(member, channel, permission);
    
    /// <summary>
    /// Returns if a given member has a channel permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetCategoryChannel channel, PlanetMember member, ChatChannelPermission permission) =>
        await _permissionsService.HasPermissionAsync(member, channel, permission);
    
    /// <summary>
    /// Deletes the given category
    /// </summary>
    public async Task DeleteAsync(PlanetCategoryChannel category)
    {
        category.IsDeleted = true;
        _db.PlanetCategoryChannels.Update(category);
        await _db.SaveChangesAsync();
    }
    
    public static readonly Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");
    
    /// <summary>
    /// Validates that a given name is allowable
    /// </summary>
    public static TaskResult ValidateName(string name)
    {
        if (name.Length > 32)
        {
            return new TaskResult(false, "Planet names must be 32 characters or less.");
        }

        if (!nameRegex.IsMatch(name))
        {
            return new TaskResult(false, "Planet names may only include letters, numbers, dashes, and underscores.");
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

    /// <summary>
    /// Validates the parent and position of this category
    /// </summary>
    public static async Task<TaskResult> ValidateParentAndPosition(ValourDB db, Valour.Database.PlanetCategory category)
    {
        if (category.ParentId != null)
        {
            var parent = await db.PlanetCategoryChannels.FindAsync(category.ParentId);
            if (parent == null) return new TaskResult(false, "Could not find parent");
            if (parent.PlanetId != category.PlanetId) return new TaskResult(false, "Parent category belongs to a different planet");
            if (parent.Id == category.Id) return new TaskResult(false, "Cannot be own parent");

            // Automatically determine position in this case
            if (category.Position < 0)
            {
                category.Position = (ushort)await db.PlanetChannels.CountAsync(x => x.ParentId == category.ParentId);
            }
            else
            {
                // Ensure position is not already taken
                if (!await HasUniquePosition(db, category))
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

                loop_parent = await db.PlanetCategoryChannels.FindAsync(loop_parent.ParentId);
            }
        }
        else
        {
            if (category.Position < 0)
            {
                category.Position = (ushort)await db.PlanetChannels.CountAsync(x => x.PlanetId == category.PlanetId && x.ParentId == null);
            }
            else
            {
                // Ensure position is not already taken
                if (!await HasUniquePosition(db, category))
                    return new TaskResult(false, "The position is already taken.");
            }
        }

        return TaskResult.SuccessResult;
    }
}