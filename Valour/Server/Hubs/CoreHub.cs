using System.Net;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using Valour.Shared.Authorization;
using Valour.Shared;
using Valour.Server.Hubs;
using Valour.Shared.Models;
using Valour.Shared.Models.Calls;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2024 Valour Software LLC
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
    private readonly ChannelService _channelService;
    private readonly CallService _callService;
    private readonly TokenService _tokenService;
    private readonly IConnectionMultiplexer _redis;

    public CoreHub(
        ValourDb db, 
        CoreHubService hubService, 
        UserOnlineService onlineService,
        PlanetMemberService memberService,
        TokenService tokenService,
        IConnectionMultiplexer redis, 
        CallService callService, 
        ChannelService channelService)
    {
        _db = db;
        _hubService = hubService;
        _onlineService = onlineService;
        _redis = redis;
        _callService = callService;
        _channelService = channelService;
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

        ConnectionTracker.ConnectionIdentities[Context.ConnectionId] = authToken;

        return new TaskResult(true, "Authenticated with SignalR hub successfully.");
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        await ConnectionTracker.RemovePrimaryConnection(Context, _redis);
        ConnectionTracker.RemoveAllMemberships(Context);

        await base.OnDisconnectedAsync(exception);
    }
    
    /// <summary>
    /// Primary node connection for user-wide events
    /// </summary>
    public async Task<TaskResult> JoinUser(bool isPrimary)
    {
        var authToken = ConnectionTracker.GetToken(Context.ConnectionId);
        if (authToken == null) return new TaskResult(false, "Failed to connect to User: SignalR was not authenticated.");

        var groupId = $"u-{authToken.UserId}";

        ConnectionTracker.TrackGroupMembership(groupId, Context);
        
        if (isPrimary)
            await ConnectionTracker.AddPrimaryConnection(authToken.UserId, Context, _redis);

        await Groups.AddToGroupAsync(Context.ConnectionId, groupId);

        return new TaskResult(true, "Connected to user " + groupId);
    }
    
    public async Task LeaveUser()
    {
        var authToken = ConnectionTracker.GetToken(Context.ConnectionId);
        if (authToken == null) return;

        var groupId = $"u-{authToken.UserId}";

        ConnectionTracker.UntrackGroupMembership(groupId, Context);
        await ConnectionTracker.RemovePrimaryConnection(Context, _redis);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
    }

    public async Task<TaskResult> JoinPlanet(long planetId)
    {
        var authToken = ConnectionTracker.GetToken(Context.ConnectionId);
        if (authToken == null) return new TaskResult(false, "Failed to connect to Planet: SignalR was not authenticated.");

        PlanetMember member = await _memberService.GetByUserAsync(authToken.UserId, planetId);

        // If the user is not a member, cancel
        if (member == null)
        {
            return new TaskResult(false, "Failed to connect to Planet: You are not a member.");
        }
        
        var groupId = $"p-{planetId}";
        ConnectionTracker.TrackGroupMembership(groupId, Context);

        // Add to planet group
        await Groups.AddToGroupAsync(Context.ConnectionId, groupId);


        return new TaskResult(true, "Connected to planet " + planetId);
    }

    public async Task LeavePlanet(long planetId) {
        var groupId = $"p-{planetId}";
        ConnectionTracker.UntrackGroupMembership(groupId, Context);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
    }


    public async Task<TaskResult> JoinChannel(long channelId)
    {
        var authToken = ConnectionTracker.GetToken(Context.ConnectionId);
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

        ConnectionTracker.TrackGroupMembership(groupId, Context);
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
        ConnectionTracker.UntrackGroupMembership(groupId, Context);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);

        return TaskResult.SuccessResult;
    }


    public async Task JoinInteractionGroup(long planetId)
    {
        var authToken = ConnectionTracker.GetToken(Context.ConnectionId);
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
            var authToken = ConnectionTracker.GetToken(Context.ConnectionId);
            if (authToken is null)
                return "pong: not authenticated";
            
            await _onlineService.UpdateOnlineState(authToken.UserId);
        }
        
        return "pong";
    }

    public AuthToken GetCurrentToken()
    {
        return ConnectionTracker.GetToken(Context.ConnectionId);
    }
    
    ///////////
    // Calls //
    ///////////

    #region Calls

    public async Task<TaskResult<LiveCallModel>> JoinPlanetLiveCall(long planetId, long channelId)
    {
        var authToken = GetCurrentToken();
        if (authToken == null) return TaskResult<LiveCallModel>.FromFailure("Unauthorized", HttpStatusCode.Unauthorized);
        
        // Check if the user is a member of the planet
        var member = await _memberService.GetByUserAsync(authToken.UserId, planetId);
        if (member == null)
            return TaskResult<LiveCallModel>.FromFailure("You are not a member of this planet.", HttpStatusCode.Forbidden);

        var channel = await _channelService.GetChannelAsync(planetId, channelId);
        if (channel is null)
            return TaskResult<LiveCallModel>.FromFailure("Channel not found.", HttpStatusCode.NotFound);
        
        if (channel.ChannelType != ChannelTypeEnum.PlanetVoice)
            return TaskResult<LiveCallModel>.FromFailure("Channel is not a voice channel.", HttpStatusCode.BadRequest);

        // Check if the user has permission to join the channel
        if (!await _channelService.HasPermissionAsync(channel, member, VoiceChannelPermissions.Join))
            return TaskResult<LiveCallModel>.FromFailure("You do not have permission to join this channel.", HttpStatusCode.Forbidden);
        
        // Now use service
        var result = await _callService.JoinPlanetLiveCallAsync(channel, member);

        return result;
    }

    #endregion
}

