using Valour.Server.Database;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Shared.Authorization;

namespace Valour.Server.Services;

public class PlanetCategoryService
{
    private readonly ValourDB _db;
    private readonly PlanetService _planetService;
    private readonly PlanetMemberService _planetMemberService;
    private readonly PermissionsService _permissionsService;
    
    public PlanetCategoryService(
        ValourDB db, 
        PlanetService planetService, 
        PlanetMemberService planetMemberService,
        PermissionsService permissionsService)
    {
        _db = db;
        _planetService = planetService;
        _planetMemberService = planetMemberService;
        _permissionsService = permissionsService;
    }

    /// <summary>
    /// Returns the category with the given id
    /// </summary>
    public async ValueTask<PlanetCategoryChannel> GetAsync(long id) =>
        await _db.PlanetCategoryChannels.FindAsync(id);

    /// <summary>
    /// Returns the children of the category with the given id
    /// </summary>
    public async Task<List<PlanetChannel>> GetChildrenAsync(long id) =>
        await _db.PlanetChannels.Where(x => x.Id == id).ToListAsync();

    /// <summary>
    /// Returns the children of the category with the given id
    /// </summary>
    public async Task<List<PlanetChannel>> GetChildrenAsync(PlanetCategoryChannel category) =>
        await GetChildrenAsync(category.Id);

    /// <summary>
    /// Returns if a given member has a channel permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetCategoryChannel channel, PlanetMember member, CategoryPermission permission) =>
        await _permissionsService.HasPermissionAsync(member, channel, permission);
    
    /// <summary>
    /// Returns if a given member has a channel permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetCategoryChannel channel, PlanetMember member, ChatChannelPermission permission) =>
        await _permissionsService.HasPermissionAsync(member, channel, permission);
    
    /// <summary>
    /// Deletes the given category
    /// </summary>
    public async Task DeleteAsync(PlanetCategoryChannel category)
    {
        category.IsDeleted = true;
        _db.PlanetCategoryChannels.Update(category);
        await _db.SaveChangesAsync();
    }
}