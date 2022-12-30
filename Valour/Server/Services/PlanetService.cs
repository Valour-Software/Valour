using Valour.Database.Context;
using Valour.Server.Database;
using Valour.Shared;

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
    /// Returns the roles for the given planet
    /// </summary>
    public async Task<ICollection<PlanetRole>> GetRolesAsync(Planet planet) =>
        await _db.PlanetRoles.Where(x => x.PlanetId == planet.Id)
            .Select(x => x.ToModel())
            .ToListAsync();

    /// <summary>
    /// Soft deletes the given planet
    /// </summary>
    public async Task DeleteAsync(Planet planet)
    {
        var entity = planet.ToDatabase();
        entity.IsDeleted = true;
        
        _db.Planets.Update(entity);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Creates a planet or updates it if it
    /// already exists
    /// </summary>
    public async Task<TaskResult<Planet>> CreateOrUpdateAsync(Planet planet)
    {
        var old = await _db.Planets.FindAsync(planet.Id);
        
        // Validate name
        var nameValid = ValidateName(planet.Name);
        if (!nameValid.Success)
            return new TaskResult<Planet>(false, nameValid.Message);

        // Validate description
        var descValid = ValidateDescription(planet.Description);
        if (!descValid.Success)
            return new TaskResult<Planet>(false, descValid.Message);
        
        // Validate owner
        var owner = await _db.Users.FindAsync(planet.OwnerId);
        if (owner is null)
            return new TaskResult<Planet>(false, "Owner does not exist.");

        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            /////////////////////////////////////
            // Logic for creating a new planet //
            /////////////////////////////////////
            if (old is null)
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
                    Name = "everyone"
                };

                await _db.PlanetCategoryChannels.AddAsync(category);
                await _db.PlanetChatChannels.AddAsync(channel);
                await _db.PlanetRoles.AddAsync(defaultRole);

                await _db.SaveChangesAsync();

                planet.PrimaryChannelId = channel.Id;
                planet.DefaultRoleId = defaultRole.Id;

                _db.Planets.Add(planet.ToDatabase());
            }
            //////////////////////////////////////////
            // Logic for editing an existing planet //
            //////////////////////////////////////////
            else
            {
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

                _db.Planets.Update(planet.ToDatabase());
            }

            await _db.SaveChangesAsync();

            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e, e.Message);
            return new TaskResult<Planet>(false, "Error adding planet to database.");
        }

        _coreHub.NotifyPlanetChange(planet);
        
        return new TaskResult<Planet>(true, "Planet added successfully.", planet);
    }
    
    //////////////////////
    // Validation Logic //
    //////////////////////

    private static readonly Regex NameRegex = new Regex(@"^[\.a-zA-Z0-9 _-]+$");

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