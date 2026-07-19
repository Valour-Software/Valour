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
    private List<PlanetListInfo> _cachedDiscoverables;

    public async Task<List<PlanetListInfo>> GetDiscoveryPlanetsAsync()
    {
        var official = await _db.Planets.AsNoTracking()
            .Where(x => x.Discoverable && x.Public
                                       && (!x.Nsfw)) // do not allow weirdos in discovery
            .Select(PlanetListInfoSelector)
            .OrderByDescending(x => x.MemberCount)
            .Take(30)
            .ToListAsync();

        var federated = await GetFederatedDiscoveryAsync(30);

        return official.Concat(federated)
            .OrderByDescending(x => x.MemberCount)
            .Take(30)
            .ToList();
    }

    /// <summary>
    /// Discoverable planets hosted on active community nodes (hub mode only).
    /// Projected into PlanetListInfo with NodeDomain set so the client badges
    /// them and routes joins to the node.
    /// </summary>
    private async Task<List<PlanetListInfo>> GetFederatedDiscoveryAsync(int take)
    {
        if (Valour.Config.Configs.FederationConfig.Current?.HubEnabled != true)
            return new List<PlanetListInfo>();

        return await (from stub in _db.FederatedPlanetStubs.AsNoTracking()
                      join node in _db.FederatedNodes.AsNoTracking() on stub.NodeDomain equals node.Domain
                      where stub.Public && stub.Discoverable && !stub.Nsfw
                            && node.Status == Valour.Database.FederatedNodeStatus.Active
                      orderby stub.MemberCount descending
                      select new PlanetListInfo
                      {
                          Id = stub.Id,
                          PlanetId = stub.Id,
                          Name = stub.Name,
                          Description = stub.Description,
                          MemberCount = stub.MemberCount,
                          Discoverable = true,
                          NodeDomain = stub.NodeDomain,
                      }).Take(take).ToListAsync();
    }
    
    public async Task<PlanetListInfo> GetPlanetInfoAsync(long planetId)
    {
        var official = await _db.Planets.AsNoTracking()
            .Where(x => x.Id == planetId && x.Public && !x.IsDeleted) // only public planets
            .Select(PlanetListInfoSelector)
            .FirstOrDefaultAsync();

        if (official is not null)
            return official;

        // Federated planets only have a small hub-side stub. Returning it from
        // the same public-info endpoint lets discovery cards and direct links
        // reach the domain-warning/join flow instead of failing as a 404.
        if (Valour.Config.Configs.FederationConfig.Current?.HubEnabled != true)
            return null;

        return await (from stub in _db.FederatedPlanetStubs.AsNoTracking()
                      join node in _db.FederatedNodes.AsNoTracking() on stub.NodeDomain equals node.Domain
                      where stub.Id == planetId && stub.Public && stub.Discoverable
                            && node.Status == Valour.Database.FederatedNodeStatus.Active
                      select new PlanetListInfo
                      {
                          Id = stub.Id,
                          PlanetId = stub.Id,
                          Name = stub.Name,
                          Description = stub.Description,
                          MemberCount = stub.MemberCount,
                          Discoverable = stub.Discoverable,
                          NodeDomain = stub.NodeDomain,
                      }).FirstOrDefaultAsync();
    }
    
    private static readonly Expression<Func<Valour.Database.Planet, PlanetListInfo>> PlanetListInfoSelector = x => new PlanetListInfo
    {
        Id = x.Id,
        PlanetId = x.Id,
        Name = x.Name,
        Description = x.Description,
        HasCustomIcon = x.HasCustomIcon,
        SelfHostedMedia = x.SelfHostedMedia,
        SelfHostedVoice = x.SelfHostedVoice,
        HasAnimatedIcon = x.HasAnimatedIcon,
        HasCustomBackground = x.HasCustomBackground,
        Discoverable = x.Discoverable,
        MemberCount = x.Members.Count(m => !m.IsDeleted),
        Version = x.Version,
        Tags = x.Tags.Select(t => new PlanetTag
        {
            Id = t.Id,
            Name = t.Name,
            Slug = t.Slug
        }).ToList()
    };
    
    /// <summary>
    /// Returns discoverable planets
    /// </summary>
    public async Task<List<PlanetListInfo>> GetDiscoverablesAsync()
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
    public async Task<QueryResponse<PlanetListInfo>> QueryDiscoverablePlanetsAsync(QueryRequest queryRequest)
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

        // Surface community-hosted planets alongside official ones. They live in
        // a separate stub table, so (for now) they're merged onto the first
        // page rather than interleaved across pagination.
        if (skip == 0)
        {
            var federated = await GetFederatedDiscoveryAsync(50);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowered = search.ToLower();
                federated = federated
                    .Where(f => (f.Name ?? "").ToLower().Contains(lowered)
                                || (f.Description ?? "").ToLower().Contains(lowered))
                    .ToList();
            }

            if (federated.Count > 0)
            {
                items = items.Concat(federated).ToList();
                totalCount += federated.Count;
            }
        }

        return new QueryResponse<PlanetListInfo>
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
            {
                var planetRoleIds = planetRoles.Select(x => x.Id).ToHashSet();
                var orderedRoleIds = order.ToHashSet();

                var missing = planetRoleIds.Except(orderedRoleIds).Take(10);
                var extra = orderedRoleIds.Except(planetRoleIds).Take(10);

                return new TaskResult(false,
                    $"Your order does not contain all the planet roles. Missing: [{string.Join(",", missing)}], Extra: [{string.Join(",", extra)}]");
            }
            
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
        data.Emojis = hostedPlanet.Emojis.List;
        data.Rules = hostedPlanet.Rules.List;
        data.VoiceParticipants = hostedPlanet.GetAllVoiceParticipants();

        return data;
    }
    
    /// <summary>
    /// Returns member info for the given planet, paged by the page index
    /// </summary>
    public async Task<PlanetMemberInfo> GetMemberInfoAsync(long planetId, int page = 0)
    {
        var cutoff = DateTime.UtcNow - PlanetMemberService.OneDayConnectionWindow;

        // Constructing base query
        var baseQuery = _db.PlanetMembers
            .Include(x => x.User)
            .AsNoTracking()
            .Where(x => x.PlanetId == planetId &&
                        x.TimeLastConnected > cutoff);
        
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

    public async Task<int> GetMemberCountAsync(long planetId)
    {
        return await _db.PlanetMembers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.PlanetId == planetId && !x.IsDeleted)
            .CountAsync();
    }

    /// <summary>
    /// Snapshot of recently active members for presence hints.
    /// "Chatting" means active within the window and not appearing offline.
    /// </summary>
    public async Task<PlanetPresenceSummary> GetPresenceSummaryAsync(long planetId, int avatarCount = 5)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-15);

        var activeQuery = _db.PlanetMembers
            .AsNoTracking()
            .Where(x => x.PlanetId == planetId &&
                        x.TimeLastConnected > cutoff &&
                        x.User.TimeLastActive > cutoff &&
                        x.User.UserStateCode != 1); // Respect explicit offline/invisible

        var count = await activeQuery.CountAsync();

        var recentUsers = await activeQuery
            .OrderByDescending(x => x.User.TimeLastActive)
            .Take(avatarCount)
            .Select(x => x.User)
            .ToListAsync();

        return new PlanetPresenceSummary()
        {
            ChattingCount = count,
            Avatars = recentUsers.Select(x => new PresenceAvatar()
            {
                Name = x.Name,
                AvatarUrl = ISharedUser.GetAvatar(x.ToModel(), AvatarFormat.Webp64)
            }).ToList()
        };
    }
    
    public async Task<Dictionary<long, int>> GetRoleMembershipCountsAsync(long planetId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        
        var roleMemberships = _db.PlanetMembers.Where(x => x.PlanetId == planetId)
            .Select(x => x.RoleMembership).AsAsyncEnumerable();

        var counts = new int[256];
        
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
    public async Task<TaskResult> DeleteAsync(long planetId)
    {
        var migrationGuard = await MigrationLock.GuardAsync(_db, planetId);
        if (!migrationGuard.Success)
            return migrationGuard;

        var entity = await _db.Planets.FindAsync(planetId);
        if (entity is null)
        {
            _logger.LogWarning("Tried to delete planet {PlanetId} but it does not exist.", planetId);
            return TaskResult.SuccessResult;
        }

        entity.IsDeleted = true;

        _db.Planets.Update(entity);
        await _db.SaveChangesAsync();

        var model = entity.ToModel();

        _coreHub.NotifyPlanetDelete(model);

        // Remove from hosted planet cache so the server stops serving it
        _hostedPlanetService.Remove(planetId);
        return TaskResult.SuccessResult;
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

        if (old.LockedForMigration)
            return new TaskResult<Planet>(false, MigrationLock.Message);

        await using var tran = await _db.Database.BeginTransactionAsync();
        
        try
        {
            var dbPlanet = planet.ToDatabase(old);
            
            if (planet.Tags is not null && planet.Tags.Count > 0)
            {
                var existingTagIds = dbPlanet.Tags.Select(t => t.Id).ToHashSet();
                var newTagIds = planet.Tags.Select(t => t.Id).ToHashSet();

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
        if (description is not null && description.Length > 4096)
        {
            return new TaskResult(false, "Description must be under 4096 characters.");
        }

        return TaskResult.SuccessResult;
    }

    ////////////
    // Vanity //
    ////////////

    public async Task<TaskResult> CheckVanityAsync(long planetId, string name)
    {
        name = name?.Trim().ToLowerInvariant();

        var validation = VanityUtils.ValidateVanity(name);
        if (!validation.Success)
            return validation;

        var taken = await _db.Planets
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Vanity == name && x.Id != planetId);

        return taken
            ? TaskResult.FromFailure("That name is already taken.")
            : TaskResult.SuccessResult;
    }

    public async Task<TaskResult> SetVanityAsync(long planetId, string name)
    {
        var dbPlanet = await _db.Planets.FirstOrDefaultAsync(x => x.Id == planetId);
        if (dbPlanet is null)
            return TaskResult.FromFailure("Planet not found.");

        if (string.IsNullOrWhiteSpace(name))
        {
            dbPlanet.Vanity = null;
        }
        else
        {
            name = name.Trim().ToLowerInvariant();

            var check = await CheckVanityAsync(planetId, name);
            if (!check.Success)
                return check;

            dbPlanet.Vanity = name;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The unique index is the race arbiter
            return TaskResult.FromFailure("That name was just taken.");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to set vanity for planet {PlanetId}", planetId);
            return TaskResult.FromFailure("Failed to set vanity name.");
        }

        _coreHub.NotifyPlanetChange(dbPlanet.ToModel());

        return TaskResult.SuccessResult;
    }

    public async Task<long?> ResolveVanityAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        name = name.Trim().ToLowerInvariant();

        var planet = await _db.Planets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Vanity == name);

        return planet?.Id;
    }
}
