using System.Collections.Concurrent;
using Valour.Server.Database;
using Valour.Server.Utilities;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using PlanetRoleMember = Valour.Database.PlanetRoleMember;

namespace Valour.Server.Services;

public class PlanetService
{
    private readonly ValourDb _db;
    private readonly CoreHubService _coreHub;
    private readonly ILogger<PlanetService> _logger;
    private readonly NodeLifecycleService _nodeLifecycleService;
    private readonly HostedPlanetService _hostedPlanetService;
    private readonly PlanetPermissionService _permissionService;
    
    public PlanetService(
        ValourDb db,
        CoreHubService coreHub,
        ILogger<PlanetService> logger,
        NodeLifecycleService nodeLifecycleService,
        HostedPlanetService hostedPlanetService, PlanetPermissionService permissionService)
    {
        _db = db;
        _coreHub = coreHub;
        _logger = logger;
        _nodeLifecycleService = nodeLifecycleService;
        _hostedPlanetService = hostedPlanetService;
        _permissionService = permissionService;
    }
    
    /// <summary>
    /// Returns whether a planet exists with the given id
    /// </summary>
    public async Task<bool> ExistsAsync(long id) =>
        await _db.Planets.AnyAsync(x => x.Id == id);

    /// <summary>
    /// Returns the planet with the given id
    /// </summary>
    public async Task<Planet> GetAsync(long id)
    {
        var hosted = await _hostedPlanetService.GetRequiredAsync(id);
        return hosted.Planet;
    }

    /// <summary>
    /// Returns the default role for the given planet
    /// </summary>
    public async Task<PlanetRole> GetDefaultRole(long planetId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        return hostedPlanet.GetDefaultRole();
    }

    /// <summary>
    /// Returns the roles for the given planet id
    /// </summary>
    public async Task<List<PlanetRole>> GetRolesAsync(long planetId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        return hostedPlanet.GetRoles();
    }

    /// <summary>
    /// Returns the roles for the given planet id
    /// </summary>
    public async Task<List<long>> GetRoleIdsAsync(long planetId)
    {
        /* TODO: this
        var hosted = _hostedPlanetService.Get(planetId);
        if (hosted is not null)
        {
            if (hosted.Roles is not null)
                return hosted.Roles.Select(x => x.Id).ToList();
        }
        */
        
        return await _db.PlanetRoles.AsNoTracking()
            .Where(x => x.PlanetId == planetId)
            .OrderBy(x => x.Position) // NEEDS TO BE ORDERED
            .Select(x => x.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Returns the invites for a given planet id
    /// </summary>
    public async Task<List<PlanetInvite>> GetInvitesAsync(long planetId) => 
        await _db.PlanetInvites.AsNoTracking()
            .Where(x => x.PlanetId == planetId)
            .Select(x => x.ToModel())
            .ToListAsync();

    /// <summary>
    /// Returns the invites ids for a given planet id
    /// </summary>
    public async Task<List<string>> GetInviteIdsAsync(long planetId) =>
        await _db.PlanetInvites.AsNoTracking()
            .Where(x => x.PlanetId == planetId)
            .Select(x => x.Id)
            .ToListAsync();

    
    private DateTime _lastDiscoverableUpdate = DateTime.MinValue;
    private List<PlanetListInfo> _cachedDiscoverables;

    public async Task<List<PlanetListInfo>> GetDiscoverablesFromDb()
    {
        return await _db.Planets.AsNoTracking()
            .Where(x => x.Discoverable && x.Public
                                       && (!x.Nsfw)) // do not allow weirdos in discovery
            .Select(x => new PlanetListInfo()
            {
                PlanetId = x.Id,
                Name = x.Name,
                Description = x.Description,
                HasCustomIcon = x.HasCustomIcon,
                HasAnimatedIcon = x.HasAnimatedIcon,
                MemberCount = x.Members.Count()
            })
            .OrderByDescending(x => x.MemberCount)
            .Take(30)
            .ToListAsync();
    }
    
    /// <summary>
    /// Returns discoverable planets
    /// </summary>
    public async Task<List<PlanetListInfo>> GetDiscoverablesAsync()
    {
        if (_lastDiscoverableUpdate.AddMinutes(5) < DateTime.UtcNow || _cachedDiscoverables is null)
        {
            _cachedDiscoverables = await GetDiscoverablesFromDb();
            _lastDiscoverableUpdate = DateTime.UtcNow;
        }
        
        return _cachedDiscoverables;
    }

    /// <summary>
    /// Sets the order of planet roles to the order in which role ids are provided
    /// </summary>
    public async Task<TaskResult> SetRoleOrderAsync(long planetId, long[] orderIn)
    {
        var planetRoles = await _db.PlanetRoles.Where(x => x.PlanetId == planetId).ToArrayAsync(); 
        
        var order = new List<long>(orderIn.Length);
        
        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {

            var newPosition = 0;
            foreach (var roleId in orderIn)
            {
                if (!order.Contains(roleId))
                {
                    var existingRole = planetRoles.FirstOrDefault(x => x.Id == roleId);
                    if (existingRole is null)
                        return new TaskResult(false, $"Role {roleId} does not belong to planet {planetId}");

                    if (existingRole.Position == 0 && newPosition != 0)
                        return new TaskResult(false, "Owner role must be first in order.");

                    if (existingRole.IsDefault && newPosition != orderIn.Length - 1)
                        return new TaskResult(false, "Default role must be last in order.");

                    order.Add(roleId);

                    if (existingRole.Position != newPosition)
                    {
                        existingRole.Position = (uint)newPosition;
                        _db.PlanetRoles.Update(existingRole);
                    }
                }
                else
                {
                    return new TaskResult(false, "Role order contains duplicate role ids.");
                }

                newPosition++;
            }

            // Ensure all the roles are included in the order
            if (order.Count != planetRoles.Length)
                return new TaskResult(false, "Your order does not contain all the planet roles.");
            
            await _db.SaveChangesAsync();
            await tran.CommitAsync();
        } 
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError("Error setting role order: {Error}", e.Message);
            return new TaskResult(false, "An unexpected error occured while saving the database changes.");
        }
        
        _coreHub.NotifyRoleOrderChange(new RoleOrderEvent()
        {
            PlanetId = planetId,
            Order = order
        });
        
        return TaskResult.SuccessResult;
    }

    public async ValueTask<Channel> GetPrimaryChannelAsync(long planetId)
    {
        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
        return hosted.GetDefaultChannel();
    }

    /// <summary>
    /// Returns the channels for the given planet that the given member can access
    /// </summary>
    public async Task<SortedServerModelList<Channel,long>> GetMemberChannelsAsync(PlanetMember member) =>
      await _permissionService.GetChannelAccessAsync(member);
    
    /// <summary>
    /// Returns member info for the given planet, paged by the page index
    /// </summary>
    public async Task<PlanetMemberInfo> GetMemberInfoAsync(long planetId, int page = 0)
    {
        // Constructing base query
        var baseQuery = _db.PlanetMembers
            .Include(x => x.User)
            .AsNoTracking()
            .Where(x => x.PlanetId == planetId);
        
        var totalCount = await baseQuery.CountAsync();
        var data = await baseQuery
            .OrderBy(x => x.Id)
            .Skip(page * 100)
            .Take(100)
            .Select(x => new PlanetMemberData
            {
                Member = x.ToModel(),
                RoleIds = x.RoleMembership.OrderBy(rm => rm.Role.Position).Select(rm => rm.RoleId).ToList()
            })
            .ToListAsync();

        return new PlanetMemberInfo()
        {
            Members = data,
            TotalCount = totalCount
        };
    }
    
    public async Task<Dictionary<long, int>> GetRoleMembershipCountsAsync(long planetId)
    {
        var query = _db.PlanetRoleMembers
            .AsNoTracking()
            .Where(x => x.PlanetId == planetId)
            .GroupBy(x => x.RoleId)
            .Select(x => new
            {
                RoleId = x.Key,
                Count = x.Count()
            });
        
        return await query.ToDictionaryAsync(x => x.RoleId, x => x.Count);
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

        planet.Description ??= "A new planet!";
        
        try
        {
            planet.Id = IdManager.Generate();

            // Create general category
            var category = new Valour.Database.Channel()
            {
                Planet = planet,
                
                Id = IdManager.Generate(),
                Name = "General",
                ParentId = null,
                Description = "General category",
                RawPosition = 0,
                
                ChannelType = ChannelTypeEnum.PlanetCategory
            };
            
            // Create general chat channel
            var chatChannel = new Valour.Database.Channel()
            {
                Planet = planet,
                Parent = category,
                
                Id = IdManager.Generate(),
                Name = "General",
                Description = "General chat channel",
                RawPosition = 0,
                IsDefault = true,
                
                ChannelType = ChannelTypeEnum.PlanetChat
            };

            planet.Channels = new List<Valour.Database.Channel>()
            {
                category,
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
            
            // Create the owner role
            var ownerRole = new Valour.Database.PlanetRole()
            {
                Planet = planet,
                Id = IdManager.Generate(),
                Position = 0,
                Color = "#bf06fd",
                Name = "Owner",
                Permissions = Permission.FULL_CONTROL,
                ChatPermissions = Permission.FULL_CONTROL,
                CategoryPermissions = Permission.FULL_CONTROL,
                VoicePermissions = Permission.FULL_CONTROL,
                IsAdmin = true,
                AnyoneCanMention = true,
                Bold = true,
                IsDefault = false,
            };

            planet.Roles = new List<Valour.Database.PlanetRole>()
            {
                defaultRole,
                ownerRole
            };
            
            // Create role combo key (ids go up over time so we order them accordingly)
            var rolesKey = PlanetPermissionService.GenerateRoleComboKey([defaultRole.Id, ownerRole.Id]);

            // Create owner member
            var member = new Valour.Database.PlanetMember()
            {
                Planet = planet,
                
                Id = IdManager.Generate(),
                Nickname = user.Name,
                UserId = user.Id,
                RoleHashKey = rolesKey
            };

            planet.Members = new List<Valour.Database.PlanetMember>()
            {
                member
            };

            // Create owner role membership
            var defaultRoleMember = new Valour.Database.PlanetRoleMember()
            {
                Planet = planet,
                Member = member,
                Role = defaultRole,

                Id = IdManager.Generate(),
                UserId = user.Id,
            };
            
            var ownerRoleMember = new Valour.Database.PlanetRoleMember()
            {
                Planet = planet,
                Member = member,
                Role = ownerRole,

                Id = IdManager.Generate(),
                UserId = user.Id,
            };

            planet.RoleMembers = new List<PlanetRoleMember>()
            {
                defaultRoleMember,
                ownerRoleMember
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
        
        Valour.Database.Channel category = null;
        
        if (categoryId is not null)
        {
            category = await _db.Channels.FindAsync(categoryId);
            if (category is null || category.ChannelType != ChannelTypeEnum.PlanetCategory)
                return new TaskResult(false, "Category not found.");
        }

        var insert = await _db.Channels.FindAsync(insertId);
        if (insert is null)
            return new TaskResult(false, "Child to insert not found.");
        
        if (insert.ParentId == categoryId)
            return new TaskResult(false, "This child is already in the category.");
        
        // Ensure we are not putting a category into one of its own children
        if (category is not null)
        {
            var parentId = category.ParentId;
            while (parentId is not null)
            {
                var parent = await _db.Channels.FindAsync(parentId);
                if (parent is null)
                    return new TaskResult(false, "Error in hierarchy.");

                if (parent.ParentId == insertId)
                    return new TaskResult(false, "This would result in a circular hierarchy.");
                
                parentId = parent.ParentId;
            }
        }

        var children = await _db.Channels
            .Where(x => x.ParentId == categoryId && x.PlanetId == insert.PlanetId)
            .OrderBy(x => x.RawPosition)
            .Select(x =>
            new {
                Id = x.Id, ChannelType = x.ChannelType
            })
            .ToListAsync();

        var position = (uint)(inPosition ?? children.Count + 1);
        
        // If unspecified or too high, set to next position
        if (position < 0 || position > children.Count)
        {
            position = (uint)(children.Count + 1);
        }
        
        var oldCategoryId = insert.ParentId;
        List<ChannelOrderData> oldCategoryOrder = null;
        
        // Positions for new category
        List<ChannelOrderData> newCategoryOrder = new();
        
        await using var trans = await _db.Database.BeginTransactionAsync();
        
        try
        {
            if (oldCategoryId is not null)
            {
                var oldCategory = await _db.Channels.FindAsync(insert.ParentId);
                if (oldCategory is null || oldCategory.ChannelType != ChannelTypeEnum.PlanetCategory)
                    return new TaskResult(false, "Error getting old parent category.");

                var oldCategoryChildren = await _db.Channels
                    .Where(x => x.ParentId == oldCategory.Id)
                    .OrderBy(x => x.RawPosition)
                    .ToListAsync();

                // Remove from old category
                oldCategoryChildren.RemoveAll(x => x.Id == insertId);

                oldCategoryOrder = new();
                
                // Update all positions
                uint opos = 0;
                foreach (var child in oldCategoryChildren)
                {
                    child.RawPosition = opos;
                    oldCategoryOrder.Add(new(child.Id, child.ChannelType));
                    opos++;
                }
            }

            insert.ParentId = categoryId;
            insert.PlanetId = insert.PlanetId;
            insert.RawPosition = position;

            var insertData = new
            {
                insert.Id,
                insert.ChannelType
            };
            
            if (position >= children.Count)
            {
                children.Add(insertData);
            }
            else
            {
                children.Insert((int)position, insertData);
            }
            
            // Update all positions
            // var pos = 0;
            foreach (var child in children)
            {
                // child.Position = pos;
                newCategoryOrder.Add(new(child.Id, child.ChannelType));
                // pos++;
            }
            
            await _db.SaveChangesAsync();
            
            // Update channel access for inserted channel if it inherits from parent
            if (insert.InheritsPerms)
            {
                await _permissionService.HandleChannelInheritanceChange(insert.ToModel());
            }

            await _db.SaveChangesAsync();

            await trans.CommitAsync();
        }
        catch (Exception e)
        {
            await trans.RollbackAsync();
            _logger.LogError(e, e.Message);
            return new TaskResult(false, "Error saving changes. Please try again later.");
        }
        
        // Fire off events for both modified categories (if applicable)
        
        // New parent
        _coreHub.NotifyCategoryOrderChange(new CategoryOrderEvent()
        {
            PlanetId = insert.PlanetId!.Value,
            CategoryId = categoryId,
            Order = newCategoryOrder
        });

        if (oldCategoryId is not null)
        {
            _coreHub.NotifyCategoryOrderChange(new CategoryOrderEvent()
            {
                PlanetId = insert.PlanetId.Value,
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