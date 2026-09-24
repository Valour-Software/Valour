using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using Valour.Shared.Authorization;
using Valour.Shared;
using Valour.Server.Hubs;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

using Valour.Shared.Villages;

namespace Valour.Server.Hubs;

public class CoreHub : Hub
{
    public const string HubUrl = "/hubs/core";

    // Allows a burst of map joins (door transitions, reconnect restore) while
    // bounding how quickly one connection can make the server load maps.
    private const int VillageJoinBurst = 10;
    private static readonly TimeSpan VillageJoinWindow = TimeSpan.FromSeconds(10);
    private static readonly ConcurrentDictionary<string, VillageJoinBudget> VillageJoinWindows = new();

    private sealed class VillageJoinBudget
    {
        public long WindowStartTicks;
        public int Count;
    }

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
    private readonly UserService _userService;
    private readonly Valour.Server.Services.Villages.VillagePresenceService _villagePresenceService;
    private readonly Valour.Server.Services.Villages.VillageRoomService _villageRoomService;

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
        HostedPlanetService hostedPlanetService,
        UserService userService,
        Valour.Server.Services.Villages.VillagePresenceService villagePresenceService,
        Valour.Server.Services.Villages.VillageRoomService villageRoomService)
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
        _userService = userService;
        _villagePresenceService = villagePresenceService;
        _villageRoomService = villageRoomService;
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

    /// <summary>
    /// Realtime groups carry the same data as HTTP routes, so a token must hold
    /// the OAuth scope those routes require. Scope failures use code 403 so the
    /// SDK can tell them apart from transport or membership failures.
    /// </summary>
    private static TaskResult ScopeFailure(string action, UserPermission permission) =>
        TaskResult.FromFailure($"Failed to {action}: the token lacks the {permission.Name} scope.", 403);

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        var authToken = _connectionTracker.GetToken(Context.ConnectionId);
        VillageJoinWindows.TryRemove(Context.ConnectionId, out _);

        if (authToken is not null)
        {
            await _channelWatchingService.ClearConnectionAsync(authToken.UserId, Context.ConnectionId);

            // Otherwise a dropped connection leaves a character standing in the
            // village until the node restarts.
            var removedVillagePresence = await _villagePresenceService.LeaveAllForUserAsync(
                authToken.UserId,
                Context.ConnectionId);
            if (removedVillagePresence)
                await _villageRoomService.ReleaseAllForUserAsync(authToken.UserId);
        }

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

        // The user group carries notifications, direct messages, calls, friend
        // events and transactions. Notifications require full control over
        // HTTP, so the combined stream does too.
        if (!authToken.HasScope(UserPermissions.FullControl))
            return ScopeFailure("connect to User", UserPermissions.FullControl);

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

        if (!authToken.HasScope(UserPermissions.Membership))
            return ScopeFailure("connect to Planet", UserPermissions.Membership);

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

        // Planet channels need the planet message scope; direct and group
        // channels need the direct message scope, matching the HTTP routes.
        var requiredScope = channel.PlanetId is null
            ? UserPermissions.DirectMessages
            : UserPermissions.Messages;
        if (!authToken.HasScope(requiredScope))
            return ScopeFailure("connect to Channel", requiredScope);

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
        else
        {
            // Direct and group channels have no permission nodes. Their member
            // list is the only thing that grants access.
            var isChannelMember = await _db.ChannelMembers
                .AsNoTracking()
                .AnyAsync(x => x.ChannelId == channelId && x.UserId == authToken.UserId);
            if (!isChannelMember)
                return new TaskResult(false, "Failed to connect to Channel: You are not a member of this channel.");
        }

        var channelModel = channel.ToModel();
        if (!await _memberService.HasPermissionAsync(member, channelModel, ChatChannelPermissions.ViewMessages))
            return new TaskResult(false, "Failed to connect to Channel: Member lacks view permissions.");

        // Temporary village rooms belong to the members currently holding a
        // lease on them, whatever the channel permissions say.
        if (!await _villageRoomService.CanAccessChannelAsync(channelModel, authToken.UserId))
            return new TaskResult(false, "Failed to connect to Channel: You are not inside this village room.");

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


    /// <summary>
    /// Joins the staff dashboard realtime group. Gated on the live ValourStaff
    /// flag rather than anything carried by the token, so revoking staff takes
    /// effect within the flag cache TTL.
    /// </summary>
    public async Task<TaskResult> JoinStaffDashboard()
    {
        var authToken = await GetValidAuthTokenAsync();
        if (authToken == null) return new TaskResult(false, "Failed to join dashboard: SignalR was not authenticated.");

        if (!authToken.HasScope(UserPermissions.FullControl))
            return ScopeFailure("join dashboard", UserPermissions.FullControl);

        var flags = await _userService.GetAccessFlagsAsync(authToken.UserId);
        if (flags is null || !flags.Value.ValourStaff)
            return new TaskResult(false, "Failed to join dashboard: You are not staff.");

        await _connectionTracker.TrackGroupMembershipAsync(DashboardHub.Group, Context);
        await Groups.AddToGroupAsync(Context.ConnectionId, DashboardHub.Group);

        return new TaskResult(true, "Connected to staff dashboard.");
    }

    public async Task LeaveStaffDashboard()
    {
        await _connectionTracker.UntrackGroupMembershipAsync(DashboardHub.Group, Context);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, DashboardHub.Group);
    }

    /// <summary>
    /// Kept so bots built against older SDKs do not fail when calling it. Embed
    /// interactions go only to the user group of the message's author, so there
    /// is no shared interaction group to join.
    /// </summary>
    public Task JoinInteractionGroup(long planetId) => Task.CompletedTask;

    /// <inheritdoc cref="JoinInteractionGroup"/>
    public Task LeaveInteractionGroup(long planetId) => Task.CompletedTask;

    /// <summary>
    /// Places the caller onto a village map and returns everyone already there.
    /// Presence is scoped to a per-map group so movement on one map does not
    /// reach clients standing on another.
    /// </summary>
    public async Task<VillagePresenceSnapshot?> JoinVillageMap(
        long planetId,
        long mapId,
        int x,
        int y,
        long? buildingId)
    {
        var authToken = await GetValidAuthTokenAsync();
        if (authToken is null || !authToken.HasScope(UserPermissions.Membership))
            return null;

        // Each join can load a map from the database, so one connection cannot
        // use it as a fast loop over arbitrary map ids.
        if (!TryConsumeVillageJoin(Context.ConnectionId))
            return null;

        var hosted = await _hostedPlanetService.TryGetAsync(planetId);
        if (hosted.HostedPlanet?.Planet.EnableVillage != true)
            return null;

        var member = await _memberService.GetByUserAsync(authToken.UserId, planetId);
        if (member is null)
            return null;

        var name = string.IsNullOrWhiteSpace(member.Nickname)
            ? member.User?.Name ?? "Member"
            : member.Nickname;

        var avatarUrl = Valour.Shared.Models.ISharedPlanetMember.GetAvatar(
            member, Valour.Shared.Models.AvatarFormat.Webp64);

        var snapshot = await _villagePresenceService.JoinMapAsync(
            planetId,
            mapId,
            authToken.UserId,
            member.Id,
            name,
            avatarUrl,
            x,
            y,
            buildingId,
            Context.ConnectionId);
        if (snapshot is null)
            return null;

        var groupId = Valour.Server.Services.Villages.VillagePresenceService.GetGroupId(planetId, mapId);
        await _connectionTracker.TrackGroupMembershipAsync(groupId, Context);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupId);

        return snapshot;
    }

    public async Task LeaveVillageMap(long planetId, long mapId)
    {
        var authToken = await GetValidAuthTokenAsync();
        if (authToken is null)
            return;

        var groupId = Valour.Server.Services.Villages.VillagePresenceService.GetGroupId(planetId, mapId);
        await _connectionTracker.UntrackGroupMembershipAsync(groupId, Context);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);

        await _villagePresenceService.LeaveMapAsync(
            planetId,
            mapId,
            authToken.UserId,
            Context.ConnectionId);
    }

    /// <summary>
    /// Reports the caller's new tile. Deliberately returns nothing: this is the
    /// hot path, and a client that has already moved locally has no use for an
    /// ack it would have to reconcile against.
    /// </summary>
    public async Task MoveInVillage(long planetId, long mapId, int x, int y, int facing, long? buildingId)
    {
        var authToken = await GetValidAuthTokenAsync();
        if (authToken is null || !authToken.HasScope(UserPermissions.Membership))
            return;

        var hosted = await _hostedPlanetService.TryGetAsync(planetId);
        if (hosted.HostedPlanet?.Planet.EnableVillage != true)
        {
            await _villagePresenceService.LeaveAllAsync(planetId, authToken.UserId, Context.ConnectionId);
            await _villageRoomService.ReleaseAllForUserAsync(authToken.UserId);
            return;
        }

        _villagePresenceService.Move(
            planetId,
            mapId,
            authToken.UserId,
            x,
            y,
            (VillageFacing)facing,
            buildingId,
            Context.ConnectionId);
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

    private static bool TryConsumeVillageJoin(string connectionId)
    {
        var budget = VillageJoinWindows.GetOrAdd(connectionId, _ => new VillageJoinBudget());
        lock (budget)
        {
            var now = DateTime.UtcNow.Ticks;
            if (now - budget.WindowStartTicks >= VillageJoinWindow.Ticks)
            {
                budget.WindowStartTicks = now;
                budget.Count = 0;
            }

            if (budget.Count >= VillageJoinBurst)
                return false;

            budget.Count++;
            return true;
        }
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
