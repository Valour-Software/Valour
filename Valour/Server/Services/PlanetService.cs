using Valour.Database.Context;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Authorization;

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
    /// Returns the planet with the given id
    /// </summary>
    public async Task<Planet> GetAsync(long id) =>
        (await _db.Planets.FindAsync(id)).ToModel();

    /// <summary>
    /// Returns the primary channel for the given planet
    /// </summary>
    public async Task<PlanetChatChannel> GetPrimaryChannelAsync(Planet planet) =>
        (await _db.PlanetChatChannels.FindAsync(planet.PrimaryChannelId)).ToModel();

    /// <summary>
    /// Returns the default role for the given planet
    /// </summary>
    public async Task<PlanetRole> GetDefaultRole(Planet planet) =>
        (await _db.PlanetRoles.FindAsync(planet.DefaultRoleId)).ToModel();

    /// <summary>
    /// Returns the roles for the given planet id
    /// </summary>
    public async Task<List<PlanetRole>> GetRolesAsync(long planetId) =>
        await _db.PlanetRoles.Where(x => x.PlanetId == planetId)
            .Select(x => x.ToModel())
            .ToListAsync();
    
    /// <summary>
    /// Returns the roles for the given planet id
    /// </summary>
    public async Task<List<long>> GetRoleIdsAsync(long planetId) =>
        await _db.PlanetRoles.Where(x => x.PlanetId == planetId)
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

                role.Position = pos;

                _db.PlanetRoles.Update(role.ToDatabase());
                roles.Add(role);

                pos++;
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
    public async Task<TaskResult<Planet>> CreateAsync(Planet planet, User user)
    {
        var baseValid = await ValidateBasic(planet);
        if (!baseValid.Success)
            return new TaskResult<Planet>(false, baseValid.Message);

        await using var tran = await _db.Database.BeginTransactionAsync();

        Valour.Database.Planet dbplanet = null;
        try
        {
            // Create general category
            var category = new Valour.Database.PlanetCategory()
            {
                Id = IdManager.Generate(),
                Name = "General",
                ParentId = null,
                PlanetId = planet.Id,
                Description = "General category",
                Position = 0
            };

            // Create general chat channel
            var channel = new Valour.Database.PlanetChatChannel()
            {
                Id = IdManager.Generate(),
                PlanetId = planet.Id,
                Name = "General",
                Description = "General chat channel",
                ParentId = category.Id,
                Position = 0
            };

            // Create default role
            var defaultRole = new Valour.Database.PlanetRole()
            {
                Id = IdManager.Generate(),
                PlanetId = planet.Id,
                Position = int.MaxValue,
                Blue = 255,
                Green = 255,
                Red = 255,
                Name = "everyone",
                Permissions = PlanetPermissions.Default,
                ChatPermissions = ChatChannelPermissions.Default,
                CategoryPermissions = CategoryPermissions.Default,
                VoicePermissions = VoiceChannelPermissions.Default
            };

            dbplanet = planet.ToDatabase();
            dbplanet.DefaultRoleId = null;
            dbplanet.PrimaryChannelId = null;

            _db.Planets.Add(dbplanet);

            await _db.SaveChangesAsync();

            await _db.PlanetCategories.AddAsync(category);
            await _db.PlanetChatChannels.AddAsync(channel);
            await _db.PlanetRoles.AddAsync(defaultRole);

            await _db.SaveChangesAsync();

            dbplanet.PrimaryChannelId = channel.Id;
            dbplanet.DefaultRoleId = defaultRole.Id;

            await _db.SaveChangesAsync();

            var member = new Valour.Database.PlanetMember()
            {
                Id = IdManager.Generate(),
                Nickname = user.Name,
                PlanetId = planet.Id,
                UserId = user.Id
            };

            var roleMember = new Valour.Database.PlanetRoleMember()
            {
                Id = IdManager.Generate(),
                PlanetId = planet.Id,
                UserId = user.Id,
                RoleId = (long)dbplanet.DefaultRoleId,
                MemberId = member.Id
            };

            await _db.PlanetMembers.AddAsync(member);
            await _db.PlanetRoleMembers.AddAsync(roleMember);

            await _db.SaveChangesAsync();

            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create planet");
            await tran.RollbackAsync();
            return new TaskResult<Planet>(false, "Failed to create planet");
        }
        
        _coreHub.NotifyPlanetChange(dbplanet.ToModel());
        
        return new TaskResult<Planet>(true, "Planet created successfully", dbplanet.ToModel());
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
        
        // Validate default role
        if (planet.DefaultRoleId != old.DefaultRoleId)
        {
            var defaultRole = await _db.PlanetRoles.FindAsync(planet.DefaultRoleId);
            if (defaultRole is null)
                return new TaskResult<Planet>(false, "Default role does not exist.");

            if (defaultRole.PlanetId != planet.Id)
                return new TaskResult<Planet>(false, "Default role belongs to a different planet.");
        }

        // Validate primary channel
        if (planet.PrimaryChannelId != old.PrimaryChannelId)
        {
            var primaryChannel = await _db.PlanetChatChannels.FindAsync(planet.PrimaryChannelId);
            if (primaryChannel is null)
                return new TaskResult<Planet>(false, "Primary channel does not exist.");

            if (primaryChannel.PlanetId != planet.Id)
                return new TaskResult<Planet>(false, "Primary channel belongs to a different planet.");
        }

        if (planet.OwnerId != old.OwnerId)
        {
            
        }

        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            _db.Planets.Update(planet.ToDatabase());
            await _db.SaveChangesAsync();
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