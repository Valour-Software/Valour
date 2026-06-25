using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using Valour.Shared.Authorization;
using Valour.Shared;
using Valour.Server.Hubs;
using Valour.Shared.Models;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Hubs;

public class CoreHub : Hub
{
    public const string HubUrl = "/hubs/core";

    private readonly ValourDb _db;
    private readonly CoreHubService _hubService;
    private readonly PlanetMemberService _memberService;
    private readonly UnreadService _unreadService;
    private readonly TokenService _tokenService;
    private readonly IConnectionMultiplexer _redis;
    private readonly SignalRConnectionService _connectionTracker;
    private readonly UserOnlineQueueService _onlineQueue;
    private readonly ChannelWatchingService _channelWatchingService;

    public CoreHub(
        ValourDb db, 
        CoreHubService hubService, 
        PlanetMemberService memberService,
        UnreadService unreadService,
        TokenService tokenService,
        IConnectionMultiplexer redis, 
        SignalRConnectionService connectionTracker,
        UserOnlineQueueService onlineQueue,
        ChannelWatchingService channelWatchingService)
    {
        _db = db;
        _hubService = hubService;
        _redis = redis;
        _connectionTracker = connectionTracker;
        _memberService = memberService;
        _unreadService = unreadService;
        _tokenService = tokenService;
        _onlineQueue = onlineQueue;
        _channelWatchingService = channelWatchingService;
    }

    public async Task<TaskResult> Authorize(string token)
    {
        // Authenticate user
        var authToken = await _tokenService.GetAsync(token);

        var result = new TaskResult(false, "Failed to authenticate connection.");
        result.Code = 401;

        if (authToken is null)
            return result;

        _connectionTracker.AddConnectionIdentity(Context.ConnectionId, authToken);

        return new TaskResult(true, "Authenticated with SignalR hub successfully.");
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        var authToken = _connectionTracker.GetToken(Context.ConnectionId);
        if (authToken is not null)
            await _channelWatchingService.ClearConnectionAsync(authToken.UserId, Context.ConnectionId);

        await _connectionTracker.RemovePrimaryConnectionAsync(Context, _redis);
        await _connectionTracker.RemoveAllMembershipsAsync(Context);

        await base.OnDisconnectedAsync(exception);
    }
    
    /// <summary>
    /// Primary node connection for user-wide events
    /// </summary>
    public async Task<TaskResult> JoinUser(bool isPrimary)
    {
        var authToken = _connectionTracker.GetToken(Context.ConnectionId);
        if (authToken == null) return new TaskResult(false, "Failed to connect to User: SignalR was not authenticated.");

        var groupId = $"u-{authToken.UserId}";

        await _connectionTracker.TrackGroupMembershipAsync(groupId, Context);
        
        if (isPrimary)
            await _connectionTracker.AddPrimaryConnectionAsync(authToken.UserId, Context, _redis);

        await Groups.AddToGroupAsync(Context.ConnectionId, groupId);

        return new TaskResult(true, "Connected to user " + groupId);
    }
    
    public async Task LeaveUser()
    {
        var authToken = _connectionTracker.GetToken(Context.ConnectionId);
        if (authToken == null) return;

        var groupId = $"u-{authToken.UserId}";

        await _connectionTracker.UntrackGroupMembershipAsync(groupId, Context);
        await _connectionTracker.RemovePrimaryConnectionAsync(Context, _redis);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
    }

    public async Task<TaskResult> JoinPlanet(long planetId)
    {
        var authToken = _connectionTracker.GetToken(Context.ConnectionId);
        if (authToken == null) return new TaskResult(false, "Failed to connect to Planet: SignalR was not authenticated.");

        PlanetMember member = await _memberService.GetByUserAsync(authToken.UserId, planetId);

        // If the user is not a member, cancel
        if (member == null)
        {
            return new TaskResult(false, "Failed to connect to Planet: You are not a member.");
        }
        
        var groupId = $"p-{planetId}";
        await _connectionTracker.TrackGroupMembershipAsync(groupId, Context, member.Id);
        _onlineQueue.Enqueue(authToken.UserId, planetIds: new[] { planetId });

        // Add to planet group
        await Groups.AddToGroupAsync(Context.ConnectionId, groupId);


        return new TaskResult(true, "Connected to planet " + planetId);
    }

    public async Task<TaskResult> LeavePlanet(long planetId) {
        var groupId = $"p-{planetId}";
        await _connectionTracker.UntrackGroupMembershipAsync(groupId, Context);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);

        return TaskResult.SuccessResult;
    }


    public async Task<TaskResult> JoinChannel(long channelId)
    {
        var authToken = _connectionTracker.GetToken(Context.ConnectionId);
        if (authToken == null) return new TaskResult(false, "Failed to connect to Channel: SignalR was not authenticated.");
        
        // Grab channel
        var channel = await _db.Channels.FindAsync(channelId);
        if (channel is null)
            return new TaskResult(false, "Failed to connect to Channel: Channel was not found.");

        PlanetMember member = null;
        if (channel.PlanetId is not null)
        {
            member = await _memberService.GetByUserAsync(authToken.UserId, channel.PlanetId.Value);

            if (member is null && ISharedChannel.PlanetChannelTypes.Contains(channel.ChannelType))
                return new TaskResult(false, "Failed to connect to Channel: You are not a member of this planet.");
        }

        if (!await _memberService.HasPermissionAsync(member, channel.ToModel(), ChatChannelPermissions.ViewMessages))
            return new TaskResult(false, "Failed to connect to Channel: Member lacks view permissions.");

        var groupId = $"c-{channelId}";

        await _connectionTracker.TrackGroupMembershipAsync(groupId, Context);
        if (channel.PlanetId is not null)
            _onlineQueue.Enqueue(authToken.UserId, planetIds: new[] { channel.PlanetId.Value });
        await Groups.AddToGroupAsync(Context.ConnectionId, groupId);
        
        var updatedState = await _unreadService.UpdateReadState(
            channelId,
            authToken.UserId,
            member?.PlanetId,
            member?.Id,
            DateTime.UtcNow);
        
        _hubService.NotifyUserChannelStateUpdate(authToken.UserId, updatedState);

        return new TaskResult(true, "Connected to channel " + channelId);
    }

    public async Task<TaskResult> LeaveChannel(long channelId) {
        var authToken = _connectionTracker.GetToken(Context.ConnectionId);
        if (authToken is not null)
            await _channelWatchingService.ClearAsync(authToken.UserId, channelId, Context.ConnectionId);

        var groupId = $"c-{channelId}";
        await _connectionTracker.UntrackGroupMembershipAsync(groupId, Context);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);

        return TaskResult.SuccessResult;
    }


    public async Task JoinInteractionGroup(long planetId)
    {
        var authToken = _connectionTracker.GetToken(Context.ConnectionId);
        if (authToken == null) return;

        PlanetMember member = await _memberService.GetByUserAsync(authToken.UserId, planetId);

        // If the user is not a member, cancel
        if (member == null)
            return;
        
        // Add to planet group
        await Groups.AddToGroupAsync(Context.ConnectionId, $"i-{planetId}");
    }

    public async Task LeaveInteractionGroup(long planetId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"i-{planetId}");

    public async Task<TaskResult> RefreshActiveChannelView(long channelId)
    {
        var authToken = _connectionTracker.GetToken(Context.ConnectionId);
        if (authToken is null)
            return new TaskResult(false, "SignalR was not authenticated.");

        if (!await CanTrackActiveChannelViewAsync(authToken.UserId, channelId))
            return new TaskResult(false, "Cannot mark this channel as active.");

        await _channelWatchingService.RefreshAsync(authToken.UserId, channelId, Context.ConnectionId);
        return TaskResult.SuccessResult;
    }

    public async Task ClearActiveChannelView(long channelId)
    {
        var authToken = _connectionTracker.GetToken(Context.ConnectionId);
        if (authToken is null)
            return;

        await _channelWatchingService.ClearAsync(authToken.UserId, channelId, Context.ConnectionId);
    }

    public Task<string> Ping(bool userState = false)
    {
        var authToken = _connectionTracker.GetToken(Context.ConnectionId);
        if (authToken is not null)
        {
            var planetIds = GetConnectedPlanetIds();
            if (userState || planetIds.Length > 0)
            {
                _onlineQueue.Enqueue(authToken.UserId, planetIds: planetIds);
            }
        }

        return Task.FromResult("pong");
    }

    private long[] GetConnectedPlanetIds()
    {
        var groups = _connectionTracker.GetConnectionGroups(Context.ConnectionId);
        if (groups.Length == 0)
            return [];

        var planetIds = new List<long>();
        foreach (var group in groups)
        {
            if (!TryGetPlanetGroupId(group, out var planetId))
                continue;

            planetIds.Add(planetId);
        }

        return planetIds.ToArray();
    }

    private static bool TryGetPlanetGroupId(string groupId, out long planetId)
    {
        planetId = 0;
        return groupId?.StartsWith("p-") == true &&
               long.TryParse(groupId.AsSpan(2), out planetId);
    }

    private async Task<bool> CanTrackActiveChannelViewAsync(long userId, long channelId)
    {
        var channelGroupId = $"c-{channelId}";
        if (_connectionTracker.GetConnectionGroups(Context.ConnectionId).Contains(channelGroupId))
            return true;

        return await _db.ChannelMembers
            .AsNoTracking()
            .AnyAsync(x => x.ChannelId == channelId && x.UserId == userId);
    }
}

