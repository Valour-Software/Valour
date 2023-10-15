using Valour.Shared;

namespace Valour.Server.Services;

public class ChannelService
{
    private readonly ValourDB _db;
    private readonly PlanetService _planetService;
    private readonly PlanetCategoryService _categoryService;
    private readonly PlanetMemberService _memberService;
    private readonly ILogger<PlanetChatChannelService> _logger;
    private readonly CoreHubService _coreHub;
    private readonly PlanetRoleService _planetRoleService;

    public ChannelService(
        ValourDB db,
        PlanetService planetService,
        PlanetCategoryService categoryService,
        PlanetMemberService memberService,
        CoreHubService coreHubService,
        ILogger<PlanetChatChannelService> logger,
        PlanetRoleService planetRoleService)
    {
        _db = db;
        _planetService = planetService;
        _categoryService = categoryService;
        _memberService = memberService;
        _logger = logger;
        _coreHub = coreHubService;
        _planetRoleService = planetRoleService;
    }
    
    /// <summary>
    /// Returns the channel with the given id
    /// </summary>
    public async ValueTask<Channel> GetAsync(long id) =>
        (await _db.Channels.FindAsync(id)).ToModel();
    
    /// <summary>
    /// Soft deletes the given channel
    /// </summary>
    public async Task<TaskResult> DeleteAsync(PlanetChatChannel channel)
    {
        var dbChannel = await _db.Channels.FindAsync(channel.Id);
        if (dbChannel.IsDefault)
            return new TaskResult(false, "You cannot delete the default channel.");
        
        dbChannel.IsDeleted = true;
        _db.PlanetChatChannels.Update(dbChannel);
        await _db.SaveChangesAsync();

        _coreHub.NotifyPlanetItemDelete(channel);

        return TaskResult.SuccessResult;
    }
}