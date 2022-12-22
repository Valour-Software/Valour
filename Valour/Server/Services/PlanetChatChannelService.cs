using IdGen;
using StackExchange.Redis;
using Valour.Server.Database;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Authorization;

namespace Valour.Server.Services;

public class PlanetChatChannelService
{
    private readonly ValourDB _db;
    private readonly PlanetService _planetService;
    private readonly PlanetCategoryService _categoryService;
    private readonly PlanetMemberService _memberService;
    private readonly PermissionsService _permissionsService;
    
    public PlanetChatChannelService(
        ValourDB db, 
        PlanetService planetService,
        PlanetCategoryService categoryService,
        PlanetMemberService memberService,
        PermissionsService permissionsService)
    {
        _db = db;
        _planetService = planetService;
        _categoryService = categoryService;
        _memberService = memberService;
        _permissionsService = permissionsService;
    }

    /// <summary>
    /// Returns the chat channel with the given id
    /// </summary>
    public async ValueTask<PlanetChatChannel> GetAsync(long id) =>
        await _db.PlanetChatChannels.FindAsync(id);

    

    /// <summary>
    /// Soft deletes the given channel
    /// </summary>
    public async Task DeleteAsync(PlanetChatChannel channel)
    {
        channel.IsDeleted = true;
        _db.PlanetChatChannels.Update(channel);
        await _db.SaveChangesAsync();
    }
}