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
    /// Returns if a given member has a channel permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetChatChannel channel, PlanetMember member, ChatChannelPermission permission) =>
        await _permissionsService.HasPermissionAsync(member, channel, permission);

    /// <summary>
    /// Deletes the given PlanetChatChannel and related data
    /// </summary>
    public void Delete(PlanetChatChannel channel)
    {
        // Remove permission nodes
        _db.PermissionsNodes.RemoveRange(
            _db.PermissionsNodes.Where(x => x.TargetId == channel.Id)
        );

        // Remove messages
        _db.PlanetMessages.RemoveRange(
            _db.PlanetMessages.Where(x => x.ChannelId == channel.Id)
        );

        // Remove channel
        _db.PlanetChatChannels.Remove(channel);
    }
}