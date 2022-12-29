using Valour.Database.Context;

namespace Valour.Server.Services;

public class PlanetService
{
    private readonly ValourDB _db;
    private readonly CoreHubService _coreHub;
    
    public PlanetService(
        ValourDB db,
        CoreHubService coreHub)
    {
        _db = db;
        _coreHub = coreHub;
    }

    /// <summary>
    /// Returns the planet with the given id
    /// </summary>
    public async Task<Planet> GetAsync(long id) =>
        (await _db.Planets.FindAsync(id)).ToModel();

    /// <summary>
    /// Returns the primary channel for the given planet
    /// </summary>
    public async ValueTask<PlanetChatChannel> GetPrimaryChannelAsync(Planet planet) =>
        (await _db.PlanetChatChannels.FindAsync(planet.PrimaryChannelId)).ToModel();

    /// <summary>
    /// Returns the default role for the given planet
    /// </summary>
    public async ValueTask<PlanetRole> GetDefaultRole(Planet planet)
    {
        planet.DefaultRole ??= await _planetRoleService.GetAsync(planet.DefaultRoleId);
        return planet.DefaultRole;
    }

    /// <summary>
    /// Returns the roles for the given planet
    /// </summary>
    public async ValueTask<ICollection<PlanetRole>> GetRolesAsync(Planet planet)
    {
        planet.Roles ??= await _db.PlanetRoles.Where(x => x.PlanetId == planet.Id).ToListAsync();
        return planet.Roles;
    }

    /// <summary>
    /// Soft deletes the given planet
    /// </summary>
    public async Task DeleteAsync(Planet planet)
    {
        planet.IsDeleted = true;
        _db.Planets.Update(planet);
        await _db.SaveChangesAsync();
    }
    
    [JsonIgnore] public static Regex nameRegex = new Regex(@"^[\.a-zA-Z0-9 _-]+$");

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

        if (!nameRegex.IsMatch(name))
        {
            return new TaskResult(false, "Planet names may only include letters, numbers, dashes, and underscores.");
        }

        return new TaskResult(true, "The given name is valid.");
    }

    /// <summary>
    /// Validates that a given description is alloweable for a planet
    /// </summary>
    public static TaskResult ValidateDescription(string description)
    {
        if (description is not null && description.Length > 128)
        {
            return new TaskResult(false, "Description must be under 128 characters.");
        }

        return TaskResult.SuccessResult;
    }
}