using IdGen;
using Microsoft.EntityFrameworkCore.Storage;
using Valour.Server.Database;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Database.Items.Users;
using Valour.Shared;
using Valour.Shared.Authorization;

namespace Valour.Server.Services;

public class PlanetService
{
    private readonly ValourDB _db;
    private readonly PlanetRoleService _planetRoleService;
    private readonly PlanetChatChannelService _chatChannelService;
    private readonly PlanetCategoryService _categoryService;
    private readonly CoreHubService _coreHub;
    
    public PlanetService(
        ValourDB db, 
        PlanetRoleService roleService,
        PlanetChatChannelService chatChannelService,
        PlanetCategoryService categoryService,
        CoreHubService coreHub)
    {
        _db = db;
        _planetRoleService = roleService;
        _chatChannelService = chatChannelService;
        _categoryService = categoryService;
        _coreHub = coreHub;
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
}