using Valour.Database.Context;
using Valour.Shared;

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
    public async Task CreateOrUpdateAsync(Planet planet)
    {
        var old = await _db.Planets.FindAsync(planet.Id);
        
        if (old is null)
        {
            _db.Planets.Add(planet.ToDatabase());
        }
        else
        {
            _db.Planets.Update(planet.ToDatabase());
        }

        await _db.SaveChangesAsync();

        _coreHub.NotifyPlanetChange(planet);
    }
}