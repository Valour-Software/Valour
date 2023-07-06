using Valour.Database.Context;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using PlanetRoleMember = Valour.Database.PlanetRoleMember;

namespace Valour.Server.Services;

public class PlanetService
{
    private readonly ValourDB _db;
    private readonly CoreHubService _coreHub;
    private readonly ILogger<PlanetService> _logger;
    
    public PlanetService(
        ValourDB db,
        CoreHubService coreHub,
        ILogger<PlanetService> logger)
    {
        _db = db;
        _coreHub = coreHub;
        _logger = logger;
    }
    
    /// <summary>
    /// Returns whether a planet exists with the given id
    /// </summary>
    public async Task<bool> ExistsAsync(long id) =>
        await _db.Planets.AnyAsync(x => x.Id == id);

    /// <summary>
    /// Returns the planet with the given id
    /// </summary>
    public async Task<Planet> GetAsync(long id) =>
        (await _db.Planets.FindAsync(id)).ToModel();

    /// <summary>
    /// Returns the primary channel for the given planet
    /// </summary>
    public async Task<PlanetChatChannel> GetPrimaryChannelAsync(long planetId) =>
        (await _db.PlanetChatChannels.FirstOrDefaultAsync(x => x.PlanetId == planetId && x.IsDefault)).ToModel();

    /// <summary>
    /// Returns the default role for the given planet
    /// </summary>
    public async Task<PlanetRole> GetDefaultRole(long planetId) =>
        (await _db.PlanetRoles.FirstOrDefaultAsync(x => x.PlanetId == planetId && x.IsDefault)).ToModel();

    /// <summary>
    /// Returns the roles for the given planet id
    /// </summary>
    public async Task<List<PlanetRole>> GetRolesAsync(long planetId) =>
        await _db.PlanetRoles.Where(x => x.PlanetId == planetId)
            .OrderBy(x => x.Position) // NEEDS TO BE ORDERED
            .Select(x => x.ToModel())
            .ToListAsync();

    /// <summary>
    /// Returns the roles for the given planet id
    /// </summary>
    public async Task<List<long>> GetRoleIdsAsync(long planetId) =>
        await _db.PlanetRoles.Where(x => x.PlanetId == planetId)
            .OrderBy(x => x.Position) // NEEDS TO BE ORDERED
            .Select(x => x.Id)
            .ToListAsync();

    /// <summary>
    /// Returns the invites for a given planet id
    /// </summary>
    public async Task<List<PlanetInvite>> GetInvitesAsync(long planetId) => 
        await _db.PlanetInvites.Where(x => x.PlanetId == planetId)
            .Select(x => x.ToModel())
            .ToListAsync();

    /// <summary>
    /// Returns the invites ids for a given planet id
    /// </summary>
    public async Task<List<long>> GetInviteIdsAsync(long planetId) =>
        await _db.PlanetInvites.Where(x => x.PlanetId == planetId)
            .Select(x => x.Id)
            .ToListAsync();

    /// <summary>
    /// Returns discoverable planets
    /// </summary>
    public async Task<List<Planet>> GetDiscoverablesAsync() =>
        await _db.Planets.Where(x => x.Discoverable && x.Public)
                         .OrderByDescending(x => x.Members.Count())
                         .Select(x => x.ToModel())       
                         .ToListAsync();

    /// <summary>
    /// Sets the order of planet roles to the order in which role ids are provided
    /// </summary>
    public async Task<TaskResult> SetRoleOrderAsync(long planetId, List<PlanetRole> order)
    {
        var totalRoles = await _db.PlanetRoles.CountAsync(x => x.PlanetId == planetId);
        if (totalRoles != order.Count)
            return new TaskResult(false, "Your order does not contain all the planet roles.");

        await using var tran = await _db.Database.BeginTransactionAsync();

        List<PlanetRole> roles = new();
        
        try
        {
            var pos = 0;

            foreach (var role in order)
            {
                if (role.PlanetId != planetId)
                    return new TaskResult(false, $"Role {role.Id} does not belong to planet {planetId}");
                
                var old = await _db.PlanetRoles.FindAsync(role.Id);
                if (old is null)
                    return new TaskResult(false, $"Role {role.Id} could not be found");
                
                // If default (everyone), force lowest position
                role.Position = old.IsDefault ? int.MaxValue : pos;
                    
                _db.Entry(old).CurrentValues.SetValues(role);
                _db.PlanetRoles.Update(old);
                roles.Add(role);

                // Don't increase position for default role
                if (role.Position != int.MaxValue)
                {
                    pos++;
                }
            }

            await _db.SaveChangesAsync();
            await tran.CommitAsync();
        }
        catch (System.Exception e)
        {
            await tran.RollbackAsync();
            return new TaskResult(false, "An unexpected error occured while saving the database changes.");
        }

        foreach (var role in roles)
        {
            _coreHub.NotifyPlanetItemChange(role);
        }

        return TaskResult.SuccessResult;
    }
    
    #region Channel Retrieval

    /// <summary>
    /// Returns the channels for the given planet
    /// </summary>
    public async Task<List<PlanetChannel>> GetChannelsAsync(long planetId) =>
        await _db.PlanetChannels.Where(x => x.PlanetId == planetId)
            .Select(x => x.ToModel())
            .ToListAsync();
    
    /// <summary>
    /// Returns the chat channels for the given planet
    /// </summary>
    public async Task<List<PlanetChatChannel>> GetChatChannelsAsync(long planetId) =>
        await _db.PlanetChatChannels.Where(x => x.PlanetId == planetId)
            .Select(x => x.ToModel())
            .ToListAsync();

    /// <summary>
    /// Returns the categories for the given planet
    /// </summary>
    public async Task<List<PlanetCategory>> GetCategoriesAsync(long planetId) =>
        await _db.PlanetCategories.Where(x => x.PlanetId == planetId)
            .Select(x => x.ToModel())
            .ToListAsync();
    
    /// <summary>
    /// Returns the voice channels for the given planet
    /// </summary>
    public async Task<List<PlanetVoiceChannel>> GetVoiceChannelsAsync(long planetId) =>
        await _db.PlanetVoiceChannels.Where(x => x.PlanetId == planetId)
            .Select(x => x.ToModel())
            .ToListAsync();
    
    #endregion

    /// <summary>
    /// Returns member info for the given planet, paged by the page index
    /// </summary>
    public async Task<PlanetMemberInfo> GetMemberInfoAsync(long planetId, int page = 0)
    {
        var members = _db.PlanetMembers
            .Include(x => x.RoleMembership)
            .ThenInclude(x => x.Role)
            .Where(x => x.PlanetId == planetId)
            .OrderBy(x => x.Id);

        var totalCount = await members.CountAsync();

        var roleInfo = await members.Select(x => new PlanetMemberData()
            {
                Member = x.ToModel(),
                User = x.User.ToModel(),
                RoleIds = x.RoleMembership.OrderBy(x => x.Role.Position).Select(rm => rm.RoleId).ToList()
            })
            .Skip(page * 100)
            .Take(100)
            .ToListAsync();
        
        return new PlanetMemberInfo()
        {
            Members = roleInfo,
            TotalCount = totalCount
        };
    }

    /// <summary>
    /// Soft deletes the given planet
    /// </summary>
    public async Task DeleteAsync(Planet planet)
    {
        var entity = await _db.Planets.FindAsync(planet.Id);
        entity.IsDeleted = true;
        
        _db.Planets.Update(entity);
        await _db.SaveChangesAsync();
        
        _coreHub.NotifyPlanetDelete(planet);
    }
    
    /// <summary>
    /// Creates the given planet
    /// </summary>
    public async Task<TaskResult<Planet>> CreateAsync(Planet model, User user)
    {
        var baseValid = await ValidateBasic(model);
        if (!baseValid.Success)
            return new TaskResult<Planet>(false, baseValid.Message);

        await using var tran = await _db.Database.BeginTransactionAsync();

        var planet = model.ToDatabase();
        
        try
        {
            planet.Id = IdManager.Generate();

            // Create general category
            var category = new Valour.Database.PlanetCategory()
            {
                Planet = planet,
                
                Id = IdManager.Generate(),
                Name = "General",
                ParentId = null,
                Description = "General category",
                Position = 0,
            };

            planet.Categories = new List<Valour.Database.PlanetCategory>()
            {
                category
            };

            // Create general chat channel
            var chatChannel = new Valour.Database.PlanetChatChannel()
            {
                Planet = planet,
                Parent = category,
                
                Id = IdManager.Generate(),
                Name = "General",
                Description = "General chat channel",
                Position = 0,
                IsDefault = true,
            };

            planet.ChatChannels = new List<Valour.Database.PlanetChatChannel>()
            {
                chatChannel
            };

            // Create default role
            var defaultRole = new Valour.Database.PlanetRole()
            {
                Planet = planet,

                Id = IdManager.Generate(),
                Position = int.MaxValue,
                Color = "#ffffff",
                Name = "everyone",
                Permissions = PlanetPermissions.Default,
                ChatPermissions = ChatChannelPermissions.Default,
                CategoryPermissions = CategoryPermissions.Default,
                VoicePermissions = VoiceChannelPermissions.Default,
                IsDefault = true,
                AnyoneCanMention = false,
            };

            planet.Roles = new List<Valour.Database.PlanetRole>()
            {
                defaultRole
            };

            // Create owner member
            var member = new Valour.Database.PlanetMember()
            {
                Planet = planet,
                
                Id = IdManager.Generate(),
                Nickname = user.Name,
                UserId = user.Id
            };

            planet.Members = new List<Valour.Database.PlanetMember>()
            {
                member
            };

            // Create owner role membership
            var roleMember = new Valour.Database.PlanetRoleMember()
            {
                Planet = planet,
                Member = member,
                Role = defaultRole,

                Id = IdManager.Generate(),
                UserId = user.Id,
            };

            planet.RoleMembers = new List<PlanetRoleMember>()
            {
                roleMember,
            };

            _db.Planets.Add(planet);
            await _db.SaveChangesAsync();
            
            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create planet");
            await tran.RollbackAsync();
            return new TaskResult<Planet>(false, "Failed to create planet");
        }

        var returnModel = planet.ToModel();
        
        _coreHub.NotifyPlanetChange(returnModel);
        
        return new TaskResult<Planet>(true, "Planet created successfully", returnModel);
    }

    /// <summary>
    /// Updates the given planet
    /// </summary>
    public async Task<TaskResult<Planet>> UpdateAsync(Planet planet)
    {
        var baseValid = await ValidateBasic(planet);
        if (!baseValid.Success)
            return new TaskResult<Planet>(false, baseValid.Message);
        
        var old = await _db.Planets.FindAsync(planet.Id);
        if (old is null)
            return new TaskResult<Planet>(false, "Planet not found.");
        
        if (old.IconUrl != planet.IconUrl)
            return new TaskResult<Planet>(false, "Use the upload API to change the planet icon.");

        if (planet.OwnerId != old.OwnerId)
        {
            return new TaskResult<Planet>(false, "You cannot change the planet owner.");
        }

        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            _db.Entry(old).CurrentValues.SetValues(planet);
            _db.Planets.Update(old);
            await _db.SaveChangesAsync();
            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e, e.Message);
            return new TaskResult<Planet>(false, "Error updating planet.");
        }

        _coreHub.NotifyPlanetChange(planet);
        
        return new TaskResult<Planet>(true, "Planet updated successfully.", planet);
    }
    
    public async Task<TaskResult> InsertChildAsync(long? categoryId, long insertId, int? inPosition = null)
    {
        if (categoryId == insertId)
            return new TaskResult(false, "A category cannot contain itself. Nice try though.");
        
        Valour.Database.PlanetCategory category = null;
        
        if (categoryId is not null)
        {
            category = await _db.PlanetCategories.FindAsync(categoryId);
            if (category is null)
                return new TaskResult(false, "Category not found.");
        }

        var insert = await _db.PlanetChannels.FindAsync(insertId);
        if (insert is null)
            return new TaskResult(false, "Child to insert not found.");
        
        // Ensure we are not putting a category into one of its own children
        if (category is not null)
        {
            var parentId = category.ParentId;
            while (parentId is not null)
            {
                var parent = await _db.PlanetCategories.FindAsync(parentId);
                if (parent is null)
                    return new TaskResult(false, "Error in hierarchy.");

                if (parent.ParentId == insertId)
                    return new TaskResult(false, "This would result in a circular hierarchy.");
                
                parentId = parent.ParentId;
            }
        }

        var children = await _db.PlanetChannels
            .Where(x => x.ParentId == categoryId)
            .OrderBy(x => x.Position)
            .ToListAsync();

        var position = inPosition ?? children.Count + 1;
        
        // If unspecified or too high, set to next position
        if (position < 0 || position > children.Count)
        {
            position = children.Count + 1;
        }
        
        var oldCategoryId = insert.ParentId;
        List<ChannelOrderData> oldCategoryOrder = null;

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
                oldCategoryOrder.Add(new(child.Id, child.Type));
                opos++;
            }
        }

        insert.ParentId = categoryId;
        insert.PlanetId = insert.PlanetId;
        insert.Position = position;
        
        if (position >= children.Count)
        {
            children.Add(insert);
        }
        else
        {
            children.Insert(position, insert);
        }

        // Positions for new category
        List<ChannelOrderData> newCategoryOrder = new();
        
        // Update all positions
        var pos = 0;
        foreach (var child in children)
        {
            child.Position = pos;
            newCategoryOrder.Add(new(child.Id, child.Type));
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
            PlanetId = insert.PlanetId,
            CategoryId = categoryId,
            Order = newCategoryOrder
        });

        if (oldCategoryId is not null)
        {
            _coreHub.NotifyCategoryOrderChange(new CategoryOrderEvent()
            {
                PlanetId = insert.PlanetId,
                CategoryId = oldCategoryId,
                Order = oldCategoryOrder,
            });
        }

        return new(true, "Success");
    }
    
    //////////////////////
    // Validation Logic //
    //////////////////////

    private static readonly Regex NameRegex = new Regex(@"^[\.a-zA-Z0-9 _-]+$");

    /// <summary>
    /// Common basic validation for planets
    /// </summary>
    private async Task<TaskResult> ValidateBasic(Planet planet)
    {
        // Validate name
        var nameValid = ValidateName(planet.Name);
        if (!nameValid.Success)
            return new TaskResult(false, nameValid.Message);

        // Validate description
        var descValid = ValidateDescription(planet.Description);
        if (!descValid.Success)
            return new TaskResult(false, descValid.Message);
        
        // Validate owner
        var owner = await _db.Users.FindAsync(planet.OwnerId);
        if (owner is null)
            return new TaskResult(false, "Owner does not exist.");

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Validates that a given name is allowable for a planet
    /// </summary>
    public static TaskResult ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new TaskResult(false, "Planet names cannot be empty.");
        }

        if (name.Length > 32)
        {
            return new TaskResult(false, "Planet names must be 32 characters or less.");
        }

        if (!NameRegex.IsMatch(name))
        {
            return new TaskResult(false, "Planet names may only include letters, numbers, dashes, and underscores.");
        }

        return new TaskResult(true, "The given name is valid.");
    }

    /// <summary>
    /// Validates that a given description is allowable for a planet
    /// </summary>
    public static TaskResult ValidateDescription(string description)
    {
        if (description is not null && description.Length > 500)
        {
            return new TaskResult(false, "Description must be under 500 characters.");
        }

        return TaskResult.SuccessResult;
    }
}