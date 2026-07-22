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
    private readonly HostedPlanetService _hostedPlanetService;

    public CoreHub(
        ValourDb db, 
        CoreHubService hubService, 
        PlanetMemberService memberService,
        UnreadService unreadService,
        TokenService tokenService,
        IConnectionMultiplexer redis, 
        SignalRConnectionService connectionTracker,
        UserOnlineQueueService onlineQueue,
        ChannelWatchingService channelWatchingService,
        HostedPlanetService hostedPlanetService)
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
        _hostedPlanetService = hostedPlanetService;
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

    /// <summary>
    /// SignalR keeps the identity supplied during <see cref="Authorize"/> in
    /// memory. Recheck its backing token on every privileged hub operation so
    /// token expiry and federation re-exchange revocation take effect without
    /// waiting for the transport to disconnect on its own.
    /// </summary>
    private async Task<AuthToken> GetValidAuthTokenAsync()
    {
        var tracked = _connectionTracker.GetToken(Context.ConnectionId);
        if (tracked is null)
            return null;

        var current = await _tokenService.GetAsync(tracked.Id);
        if (current is not null && current.UserId == tracked.UserId)
            return current;

        Context.Abort();
        return null;
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
        var authToken = await GetValidAuthTokenAsync();
        if (authToken == null) return new TaskResult(false, "Failed to connect to User: SignalR was not authenticated.");

        // Federation sessions are deliberately planet-scoped. Letting one join
        // the user-wide group would allow a modified node client to subscribe
        // to node-local notifications or direct-message events outside of the
        // federated planet, even though the normal SDK never makes this call.
        if (authToken.AppId == "FEDERATION")
            return new TaskResult(false, "Federation sessions cannot join user-wide realtime.");

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
        var authToken = await GetValidAuthTokenAsync();
        if (authToken == null) return new TaskResult(false, "Failed to connect to Planet: SignalR was not authenticated.");

        var hosted = await _hostedPlanetService.TryGetAsync(planetId);
        if (hosted.HostedPlanet is null)
            return new TaskResult(false, $"Failed to connect to Planet: Planet is hosted on {hosted.CorrectNode}.");

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
        var authToken = await GetValidAuthTokenAsync();
        if (authToken == null) return new TaskResult(false, "Failed to connect to Channel: SignalR was not authenticated.");
        
        // Grab channel
        var channel = await _db.Channels.FindAsync(channelId);
        if (channel is null)
            return new TaskResult(false, "Failed to connect to Channel: Channel was not found.");

        PlanetMember member = null;
        if (channel.PlanetId is not null)
        {
            var hosted = await _hostedPlanetService.TryGetAsync(channel.PlanetId.Value);
            if (hosted.HostedPlanet is null)
                return new TaskResult(false, $"Failed to connect to Channel: Planet is hosted on {hosted.CorrectNode}.");

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

        if (updatedState.Success)
            _hubService.NotifyUserChannelStateUpdate(authToken.UserId, updatedState.Data);

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
        var authToken = await GetValidAuthTokenAsync();
        if (authToken == null) return;

        PlanetMember member = await _memberService.GetByUserAsync(authToken.UserId, planetId);

        // If the user is not a member, cancel
        if (member == null)
            return;

        var groupId = $"i-{planetId}";
        await _connectionTracker.TrackGroupMembershipAsync(groupId, Context);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupId);
    }

    public async Task LeaveInteractionGroup(long planetId)
    {
        var groupId = $"i-{planetId}";
        await _connectionTracker.UntrackGroupMembershipAsync(groupId, Context);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
    }

    public async Task<TaskResult> RefreshActiveChannelView(long channelId)
    {
        var authToken = await GetValidAuthTokenAsync();
        if (authToken is null)
            return new TaskResult(false, "SignalR was not authenticated.");

        if (!await CanTrackActiveChannelViewAsync(authToken.UserId, channelId))
            return new TaskResult(false, "Cannot mark this channel as active.");

        await _channelWatchingService.RefreshAsync(authToken.UserId, channelId, Context.ConnectionId);
        return TaskResult.SuccessResult;
    }

    public async Task ClearActiveChannelView(long channelId)
    {
        var authToken = await GetValidAuthTokenAsync();
        if (authToken is null)
            return;

        await _channelWatchingService.ClearAsync(authToken.UserId, channelId, Context.ConnectionId);
    }

    public async Task<string> Ping(bool userState = false)
    {
        var authToken = await GetValidAuthTokenAsync();
        if (authToken is not null)
        {
            var planetIds = GetConnectedPlanetIds();
            if (userState || planetIds.Length > 0)
            {
                _onlineQueue.Enqueue(authToken.UserId, planetIds: planetIds);
            }
        }

        return "pong";
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

