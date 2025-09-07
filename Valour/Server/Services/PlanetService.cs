using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq.Expressions;
using Valour.Server.Database;
using Valour.Server.Utilities;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Queries;

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
    public async Task<ImmutableList<PlanetRole>> GetRolesAsync(long planetId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        return hostedPlanet.Roles.List;
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
    private List<ISharedPlanetListInfo> _cachedDiscoverables;

    public async Task<List<ISharedPlanetListInfo>> GetDiscoveryPlanetsAsync()
    {
        return await _db.Planets.AsNoTracking()
            .Where(x => x.Discoverable && x.Public
                                       && (!x.Nsfw)) // do not allow weirdos in discovery
            .Select(PlanetListInfoSelector)
            .OrderByDescending(x => x.MemberCount)
            .Take(30)
            .ToListAsync();
    }
    
    public async Task<ISharedPlanetListInfo> GetPlanetInfoAsync(long planetId)
    {
        return await _db.Planets.AsNoTracking()
            .Where(x => x.Id == planetId && x.Public && !x.IsDeleted) // only public planets
            .Select(PlanetListInfoSelector)
            .FirstOrDefaultAsync();
    }
    
    private static readonly Expression<Func<Valour.Database.Planet, ISharedPlanetListInfo>> PlanetListInfoSelector = x => new Valour.Sdk.Models.PlanetListInfo
    {
        Id = x.Id,
        PlanetId = x.Id,
        Name = x.Name,
        Description = x.Description,
        HasCustomIcon = x.HasCustomIcon,
        HasAnimatedIcon = x.HasAnimatedIcon,
        HasCustomBackground = x.HasCustomBackground,
        MemberCount = x.Members.Count(),
        Version = x.Version,
        TagIds = x.Tags.Select(t => t.Id).Distinct().ToList()
    };
    
    /// <summary>
    /// Returns discoverable planets
    /// </summary>
    public async Task<List<ISharedPlanetListInfo>> GetDiscoverablesAsync()
    {
        if (_lastDiscoverableUpdate.AddMinutes(5) < DateTime.UtcNow || _cachedDiscoverables is null)
        {
            _cachedDiscoverables = await GetDiscoveryPlanetsAsync();
            _lastDiscoverableUpdate = DateTime.UtcNow;
        }
        
        return _cachedDiscoverables;
    }

    /// <summary>
    /// Queries discoverable planets with filters and pagination
    /// </summary>
    public async Task<QueryResponse<ISharedPlanetListInfo>> QueryDiscoverablePlanetsAsync(QueryRequest queryRequest)
    {
        var take = queryRequest.Take;
        if (take > 50)
            take = 50;
        
        var skip = queryRequest.Skip;
        var search = queryRequest.Options?.Filters?.GetValueOrDefault("search");

        var query = _db.Planets.AsNoTracking()
            .Where(x => x.Discoverable && x.Public && !x.Nsfw);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var lowered = search.ToLower();
            query = query.Where(x => 
                EF.Functions.ILike(x.Name.ToLower(), $"%{lowered}%") ||
                EF.Functions.ILike(x.Description.ToLower(), $"%{lowered}%"));
        }

        var sortDesc = queryRequest.Options?.Sort?.Descending ?? false;
        query = queryRequest.Options?.Sort?.Field switch
        {
            "name" => sortDesc
                ? query.OrderByDescending(x => x.Name)
                : query.OrderBy(x => x.Name),
            "memberCount" => sortDesc
                ? query.OrderByDescending(x => x.Members.Count())
                : query.OrderBy(x => x.Members.Count()),
            _ => query.OrderByDescending(x => x.Members.Count())
        };

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip(skip)
            .Take(take)
            .Select(PlanetListInfoSelector)
            .ToListAsync();

        return new QueryResponse<ISharedPlanetListInfo>
        {
            Items = items,
            TotalCount = totalCount
        };
    }

    /// <summary>
    /// Sets the order of planet roles to the order in which role ids are provided
    /// </summary>
    public async Task<TaskResult> SetRoleOrderAsync(long planetId, long[] orderIn)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        
        var planetRoles = await _db.PlanetRoles.Where(x => x.PlanetId == planetId).ToArrayAsync(); 
        
        var order = new List<long>(orderIn.Length);
        
        await using var tran = await _db.Database.BeginTransactionAsync();
        
        var changedRoles = new List<PlanetRole>();

        try
        {

            uint newPosition = 0;
            foreach (var roleId in orderIn)
            {
                if (!order.Contains(roleId))
                {
                    var existingRole = planetRoles.FirstOrDefault(x => x.Id == roleId);
                    if (existingRole is null)
                        return new TaskResult(false, $"Role {roleId} does not belong to planet {planetId}");

                    if (existingRole.IsDefault && newPosition != orderIn.Length - 1)
                        return new TaskResult(false, "Default role must be last in order.");

                    order.Add(roleId);

                    if (existingRole.Position != newPosition)
                    {
                        existingRole.Position = newPosition;
                        _db.PlanetRoles.Update(existingRole);
                        
                        changedRoles.Add(existingRole.ToModel());
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

        foreach (var role in changedRoles)
        {
            hostedPlanet.UpsertRole(role);
        }
        
        // We completely dump all permissions for the planet when roles are reordered
        await _permissionService.HandleRoleOrderChange(planetId);
        
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
    
    public async ValueTask<ImmutableList<Channel>> GetAllChannelsAsync(long planetId)
    {
        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
        return hosted.Channels.List;
    }

    /// <summary>
    /// Returns the channels for the given planet that the given member can access
    /// </summary>
    public async Task<ModelListSnapshot<Channel,long>?> GetMemberChannelsAsync(long memberId) =>
      await _permissionService.GetChannelAccessAsync(memberId);

    public async Task<InitialPlanetData> GetInitialDataAsync(long planetId, long memberId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        
        var data = new InitialPlanetData();
        var channels = await GetMemberChannelsAsync(memberId);
        
        data.Channels = channels?.List ?? [];
        data.Roles = hostedPlanet.Roles.List;
        
        return data;
    }
    
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
            .Select(x => x.ToModel())
            .ToListAsync();

        return new PlanetMemberInfo()
        {
            Members = data,
            TotalCount = totalCount
        };
    }
    
    public async Task<Dictionary<long, int>> GetRoleMembershipCountsAsync(long planetId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        
        var roleMemberships = _db.PlanetMembers.Where(x => x.PlanetId == planetId)
            .Select(x => x.RoleMembership).AsAsyncEnumerable();

        var counts = new byte[256];
        
        await foreach (var membership in roleMemberships)
        {
            foreach (var roleIndex in membership.EnumerateRoleIndices())
            {
                counts[roleIndex]++;
            }
        }
        
        var result = new Dictionary<long, int>();
        
        for (var i = 0; i < counts.Length; i++)
        {
            var count = counts[i];
            
            if (count > 0)
            {
                var roleId = hostedPlanet.GetRoleIdByIndex(i);
                result.Add(roleId, count);
            }
        }
        
        return result;
    }

    /// <summary>
    /// Soft deletes the given planet
    /// </summary>
    public async Task DeleteAsync(long planetId)
    {
        var entity = await _db.Planets.FindAsync(planetId);
        if (entity is null)
        {
            _logger.LogWarning("Tried to delete planet {PlanetId} but it does not exist.", planetId);
            return;
        }
        
        entity.IsDeleted = true;
        
        _db.Planets.Update(entity);
        await _db.SaveChangesAsync();
        
        var model = entity.ToModel();
        
        _coreHub.NotifyPlanetDelete(model);
    }
    
    /// <summary>
    /// Creates the given planet
    /// </summary>
   public async Task<TaskResult<Planet>> CreateAsync(Planet model, User user, long? forceId = null)
    {
        var baseValid = await ValidateBasic(model);
        if (!baseValid.Success)
            return new TaskResult<Planet>(false, baseValid.Message);

        await using var tran = await _db.Database.BeginTransactionAsync();

        var planet = model.ToDatabase();
        
        if (model.TagIds?.Any() ?? false)
        {
            var tags = await _db.Tags
                .Where(t => model.TagIds.Contains(t.Id))
                .ToListAsync();
            
            planet.Tags = tags;
        }

        planet.Description ??= "A new planet!";
        
        try
        {
            planet.Id = forceId ?? IdManager.Generate();

            var position = new ChannelPosition();
            position = position.Append(1); // Position 1
            
            // Create general category
            var category = new Valour.Database.Channel()
            {
                Planet = planet,
                
                Id = IdManager.Generate(),
                Name = "General",
                ParentId = null,
                Description = "General category",
                RawPosition = position.RawPosition,
                
                ChannelType = ChannelTypeEnum.PlanetCategory
            };
            
            position = position.Append(1); // Position 1 inside category
            
            // Create general chat channel
            var chatChannel = new Valour.Database.Channel()
            {
                Planet = planet,
                Parent = category,
                
                Id = IdManager.Generate(),
                Name = "General",
                Description = "General chat channel",
                RawPosition = position.RawPosition,
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

            planet.Roles = new List<Valour.Database.PlanetRole>()
            {
                defaultRole,
            };
            
            // Create owner member
            var member = new Valour.Database.PlanetMember()
            {
                Planet = planet,
                
                Id = IdManager.Generate(),
                Nickname = user.Name,
                UserId = user.Id,
                RoleMembership = new PlanetRoleMembership(0x01)
            };

            planet.Members = new List<Valour.Database.PlanetMember>()
            {
                member
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
        
        var old = await _db.Planets
            .Include(p => p.Tags) 
            .FirstOrDefaultAsync(p => p.Id == planet.Id);
        
        if (old is null)
            return new TaskResult<Planet>(false, "Planet not found.");
        
        if (planet.OwnerId != old.OwnerId)
        {
            return new TaskResult<Planet>(false, "You cannot change the planet owner.");
        }

        await using var tran = await _db.Database.BeginTransactionAsync();
        
        try
        {
            var dbPlanet = planet.ToDatabase(old);
            
            if (planet.TagIds?.Any() ?? false)
            {
                var existingTagIds = dbPlanet.Tags.Select(t => t.Id).ToHashSet();
                var newTagIds = planet.TagIds.ToHashSet();

                foreach (var tag in dbPlanet.Tags.Where(t => !newTagIds.Contains(t.Id)).ToList())
                {
                    dbPlanet.Tags.Remove(tag);
                }

                var tagsToAdd = await _db.Tags
                    .Where(t => newTagIds.Contains(t.Id) && !existingTagIds.Contains(t.Id))
                    .ToListAsync();
            
                foreach (var tag in tagsToAdd)
                {
                    dbPlanet.Tags.Add(tag);
                }
            }
            else
            {
                dbPlanet.Tags.Clear();
            }
            
            _db.Planets.Update(dbPlanet);
            
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
    
    //////////////////////
    // Validation Logic //
    //////////////////////

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