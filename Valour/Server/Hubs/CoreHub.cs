using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using Valour.Shared.Authorization;
using Valour.Shared;
using Valour.Server.Hubs;

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
    private readonly UserOnlineService _onlineService;
    private readonly PlanetMemberService _memberService;
    private readonly TokenService _tokenService;
    private readonly IConnectionMultiplexer _redis;
    private readonly SignalRConnectionService _connectionTracker;

    public CoreHub(
        ValourDb db, 
        CoreHubService hubService, 
        UserOnlineService onlineService,
        PlanetMemberService memberService,
        TokenService tokenService,
        IConnectionMultiplexer redis, 
        SignalRConnectionService connectionTracker)
    {
        _db = db;
        _hubService = hubService;
        _onlineService = onlineService;
        _redis = redis;
        _connectionTracker = connectionTracker;
        _memberService = memberService;
        _tokenService = tokenService;
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
        await _connectionTracker.TrackGroupMembershipAsync(groupId, Context);

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
        
        PlanetMember member = (await _db.PlanetMembers.FirstOrDefaultAsync(
            x => x.UserId == authToken.UserId && x.PlanetId == channel.PlanetId)).ToModel();

        if (!await _memberService.HasPermissionAsync(member, channel.ToModel(), ChatChannelPermissions.ViewMessages))
            return new TaskResult(false, "Failed to connect to Channel: Member lacks view permissions.");

        var groupId = $"c-{channelId}";

        await _connectionTracker.TrackGroupMembershipAsync(groupId, Context);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupId);
        
        var channelState = await _db.UserChannelStates.FirstOrDefaultAsync(x => x.UserId == authToken.UserId && x.ChannelId == channel.Id);

        if (channelState is null)
        {
            channelState = new UserChannelState()
            {
                UserId = authToken.UserId,
                ChannelId = channelId,
                PlanetMemberId = member?.Id,
                PlanetId = member?.PlanetId
            }.ToDatabase();

            _db.UserChannelStates.Add(channelState);
        }
        
        channelState.LastViewedTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        
        _hubService.NotifyUserChannelStateUpdate(authToken.UserId, channelState.ToModel());

        return new TaskResult(true, "Connected to channel " + channelId);
    }

    public async Task<TaskResult> LeaveChannel(long channelId) {
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

    public async Task<string> Ping(bool userState = false)
    {
        if (userState)
        {
            var authToken = _connectionTracker.GetToken(Context.ConnectionId);
            if (authToken is null)
                return "pong: not authenticated";
            
            await _onlineService.UpdateOnlineState(authToken.UserId);
        }
        
        return "pong";
    }
}

