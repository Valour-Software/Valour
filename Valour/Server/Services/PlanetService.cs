using Valour.Server.Database;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Members;

namespace Valour.Server.Services;

public class PlanetService
{
    private readonly ValourDB _db;
    private readonly PlanetRoleService _planetRoleService;
    private readonly PlanetChatChannelService _chatChannelService;
    
    public PlanetService(
        ValourDB db, 
        PlanetRoleService roleService, 
        PlanetChatChannelService chatChannelService)
    {
        _db = db;
        _planetRoleService = roleService;
        _chatChannelService = chatChannelService;
    }

    /// <summary>
    /// Returns the planet with the given id
    /// </summary>
    public ValueTask<Planet> GetAsync(long id) =>
        _db.Planets.FindAsync(id);

    /// <summary>
    /// Returns the primary channel for the given planet
    /// </summary>
    public async ValueTask<PlanetChatChannel> GetPrimaryChannelAsync(Planet planet)
    {
        planet.PrimaryChannel ??= await _chatChannelService.GetAsync(planet.PrimaryChannelId);
        return planet.PrimaryChannel;
    }

    /// <summary>
    /// Returns the default role for the given planet
    /// </summary>
    public async ValueTask<PlanetRole> GetDefaultRole(Planet planet)
    {
        planet.DefaultRole ??= await _planetRoleService.GetAsync(planet.DefaultRoleId);
        return planet.DefaultRole;
    }

    public async Task<ICollection<PlanetRole>> GetRolesAsync(Planet planet)
    {
        planet.Roles ??= await _db.PlanetRoles.Where(x => x.PlanetId == planet.Id).ToListAsync();
        return planet.Roles;
    }
}