using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using Valour.Config.Configs;
using Valour.Sdk.Models.Embeds;
using Valour.Server.Hubs;
using Valour.Shared.Authorization;
using Valour.Shared.Channels;
using Valour.Shared.Models;
using Notification = Valour.Server.Models.Notification;
using Planet = Valour.Server.Models.Planet;
using User = Valour.Server.Models.User;
using UserChannelState = Valour.Server.Models.UserChannelState;

namespace Valour.Server.Services;

public class CoreHubService
{
    // Map of channelids to users typing from prev channel update
    public static ConcurrentDictionary<long, List<long>> PrevCurrentlyTyping = new();
    private static readonly ConcurrentDictionary<long, long?> ChannelToPlanetIdCache = new();
    private static readonly ConcurrentDictionary<long, long> ChannelViewUpdateTimes = new();
    private static readonly TimeSpan ChannelViewUpdateCooldown = TimeSpan.FromSeconds(2);
    private static readonly SemaphoreSlim ChannelViewUpdateSemaphore = new(4, 4);
    
    private readonly IHubContext<CoreHub> _hub;
    private readonly ValourDb _db;
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly SignalRConnectionService _connectionTracker;
    private readonly ChannelWatchingService _channelWatchingService;
    private readonly UserCacheService _userCache;
    private readonly NodeLifecycleService _nodeLifecycleService;
    private readonly ILogger<CoreHubService> _logger;

    public CoreHubService(
        ValourDb db,
        IServiceProvider serviceProvider,
        IHubContext<CoreHub> hub,
        IConnectionMultiplexer redis,
        SignalRConnectionService connectionTracker,
        ChannelWatchingService channelWatchingService,
        UserCacheService userCache,
        NodeLifecycleService nodeLifecycleService,
        ILogger<CoreHubService> logger)
    {
        _db = db;
        _hub = hub;
        _serviceProvider = serviceProvider;
        // Filtered sends finish after the calling request, so they need a scope
        // factory that stays valid when the request scope is disposed.
        _scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        _redis = redis;
        _connectionTracker = connectionTracker;
        _channelWatchingService = channelWatchingService;
        _userCache = userCache;
        _nodeLifecycleService = nodeLifecycleService;
        _logger = logger;
    }
    
    public void RelayMessage(Message message)
    {
        var groupId = $"c-{message.ChannelId}";

        if (NodeConfig.Instance.LogInfo)
            _logger.LogDebug("[{Node}] Relaying message {MessageId} to group {GroupId}",
                NodeConfig.Instance.Name, message.Id, groupId);

        // Fire-and-forget broadcast (matches pattern used by every other relay method)
        _ = _hub.Clients.Group(groupId).SendAsync("Relay", message);

        // Fire-and-forget channel state update with its own scope to avoid
        // blocking the message pipeline. Uses a separate DbContext so the
        // long-lived worker DbContext is never accessed concurrently.
        if (ShouldScheduleChannelViewUpdate(message.ChannelId))
        {
            _ = UpdateActiveChannelViewStatesAsync(message.ChannelId);
        }
    }

    private static bool ShouldScheduleChannelViewUpdate(long channelId)
    {
        var nowTicks = DateTime.UtcNow.Ticks;

        while (true)
        {
            if (ChannelViewUpdateTimes.TryGetValue(channelId, out var lastTicks))
            {
                if (nowTicks - lastTicks < ChannelViewUpdateCooldown.Ticks)
                    return false;

                if (ChannelViewUpdateTimes.TryUpdate(channelId, nowTicks, lastTicks))
                    return true;

                continue;
            }

            if (ChannelViewUpdateTimes.TryAdd(channelId, nowTicks))
                return true;
        }
    }

    private async Task UpdateChannelViewStatesAsync(long[] viewingIds, long channelId)
    {
        await ChannelViewUpdateSemaphore.WaitAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var scope = _serviceProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            await db.Database.ExecuteSqlRawAsync(
                "CALL batch_user_channel_state_update({0}, {1}, {2});",
                new object[] { viewingIds, channelId, DateTime.UtcNow },
                timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // Non-critical: channel state will self-correct when the user next opens the channel.
            _logger.LogWarning("Timed out updating channel view states for channel {ChannelId}", channelId);
        }
        catch (Exception ex)
        {
            // Non-critical: channel state will self-correct when the user next opens the channel
            _logger.LogWarning(ex, "Failed to update channel view states for channel {ChannelId}", channelId);
        }
        finally
        {
            ChannelViewUpdateSemaphore.Release();
        }
    }

    private async Task UpdateActiveChannelViewStatesAsync(long channelId)
    {
        try
        {
            var viewingIds = await _channelWatchingService.GetActiveViewingUserIdsAsync(channelId);
            if (viewingIds.Count == 0)
                return;

            await UpdateChannelViewStatesAsync(viewingIds.ToArray(), channelId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get active channel viewers for channel {ChannelId}", channelId);
        }
    }
    
    public void RelayMessageEdit(Message message)
    {
        var groupId = $"c-{message.ChannelId}";

        // Group we are sending messages to
        var group = _hub.Clients.Group(groupId);
        
        if (NodeConfig.Instance.LogInfo)
            _logger.LogDebug("[{Node}] Relaying edited message {MessageId} to group {GroupId}",
                NodeConfig.Instance.Name, message.Id, groupId);

        _ = group.SendAsync("RelayEdit", message);
    }
    
    public void RelayMessageReactionAdded(long channelId, MessageReaction reaction)
    {
        var groupId = $"c-{channelId}";

        // Group we are sending messages to
        var group = _hub.Clients.Group(groupId);

        _ = group.SendAsync("MessageReactionAdd", reaction);
    }
    
    public void RelayMessageReactionRemoved(long channelId, MessageReaction reaction)
    {
        var groupId = $"c-{channelId}";

        // Group we are sending messages to
        var group = _hub.Clients.Group(groupId);

        _ = group.SendAsync("MessageReactionRemove", reaction);
    }

    public async Task RelayFriendEvent(long targetId, FriendEventData eventData, NodeLifecycleService nodeLifecycleService)
    {
        await nodeLifecycleService.RelayUserEventAsync(targetId, NodeLifecycleService.NodeEventType.Friend, eventData);
    }

    public async Task RelayDirectMessage(Message message, NodeLifecycleService nodeLifecycleService, List<long> userIds)
    {
        foreach (var userId in userIds)
        {
            await nodeLifecycleService.RelayUserEventAsync(userId, NodeLifecycleService.NodeEventType.DirectMessage, message);
        }
    }
    
    public async Task RelayDirectMessageEdit(Message message, NodeLifecycleService nodeLifecycleService, List<long> userIds)
    {
        foreach (var userId in userIds)
        {
            await nodeLifecycleService.RelayUserEventAsync(userId, NodeLifecycleService.NodeEventType.DirectMessageEdit, message);
        }
    }

    public async Task RelayDirectMessageDelete(Message message, NodeLifecycleService nodeLifecycleService, List<long> userIds)
    {
        foreach (var userId in userIds)
        {
            await nodeLifecycleService.RelayUserEventAsync(userId, NodeLifecycleService.NodeEventType.DirectMessageDelete, message);
        }
    }

    public async Task RelayDirectCallUpdate(
        DirectCall call,
        NodeLifecycleService nodeLifecycleService,
        List<long> userIds)
    {
        foreach (var userId in userIds)
            await nodeLifecycleService.RelayUserEventAsync(
                userId,
                NodeLifecycleService.NodeEventType.DirectCall,
                call);
    }

    public async Task RelayDirectChannelUpdate(
        Channel channel,
        NodeLifecycleService nodeLifecycleService,
        IEnumerable<long> userIds)
    {
        foreach (var userId in userIds.Distinct())
            await nodeLifecycleService.RelayUserEventAsync(
                userId,
                NodeLifecycleService.NodeEventType.DirectChannel,
                channel);
    }

    public Task RelayDirectChannelRemoved(
        long channelId,
        long userId,
        NodeLifecycleService nodeLifecycleService) =>
        nodeLifecycleService.RelayUserEventAsync(
            userId,
            NodeLifecycleService.NodeEventType.DirectChannelRemoved,
            channelId);

    public void RelayNotification(Notification notif, NodeLifecycleService nodeLifecycleService)
    {
        _ = nodeLifecycleService.RelayUserEventAsync(notif.UserId, NodeLifecycleService.NodeEventType.Notification, notif);
    }
    
    public void RelayNotificationReadChange(Notification notif, NodeLifecycleService nodeLifecycleService)
    {
        _ = nodeLifecycleService.RelayUserEventAsync(notif.UserId, NodeLifecycleService.NodeEventType.Notification, notif);
    }
    
    public void RelayNotificationsCleared(long userId, NodeLifecycleService nodeLifecycleService)
    {
        _ = nodeLifecycleService.RelayUserEventAsync(userId, NodeLifecycleService.NodeEventType.NotificationsCleared, userId);
    }
    
    public void NotifyChannelsMoved(ChannelsMovedEvent eventData) => 
        _ = _hub.Clients.Group($"p-{eventData.PlanetId}").SendAsync("Channels-Moved", eventData);
    
    public void NotifyRoleMembershipHashChanges(RoleMembershipHashChange change) =>
        _ = _hub.Clients.Group($"p-{change.PlanetId}").SendAsync("RoleMembershipHash-Update", change);
    
    public void NotifyRoleOrderChange(RoleOrderEvent eventData) =>
        _ = _hub.Clients.Group($"p-{eventData.PlanetId}").SendAsync("RoleOrder-Update", eventData);

    public void ForceLogoutUser(long userId) =>
        _ = _hub.Clients.Group($"u-{userId}").SendAsync("ForceLogout", "disabled");

    public void ForceLogoutToken(string tokenId)
    {
        var connectionIds = _connectionTracker.GetConnectionsByTokenId(tokenId);
        if (connectionIds.Length == 0)
            return;

        _ = _hub.Clients.Clients(connectionIds).SendAsync("ForceLogout", "revoked");
    }

    public void NotifyUserChannelStateUpdate(long userId, UserChannelState state) =>
        _ = _hub.Clients.Group($"u-{userId}").SendAsync("UserChannelState-Update", state);

    public void NotifyVoiceSessionReplace(long userId, VoiceSessionReplaceEvent update) =>
        _ = _hub.Clients.Group($"u-{userId}").SendAsync("Voice-Session-Replace", update);

    public void NotifyVoiceChannelParticipants(long planetId, VoiceChannelParticipantsUpdate update) =>
        SendToPlanetMembers(planetId, "Voice-Channel-Participants", [update], channelId: update.ChannelId);

    // Village presence is scoped to a map group rather than the whole planet:
    // a member walking around an outdoor map should not wake up every client
    // connected to the planet. Fire-and-forget for the same reason the other
    // high-frequency notifies are - a failed movement frame is not worth an
    // exception, and the next one is milliseconds away.

    public void NotifyVillagePresenceJoined(long planetId, long mapId, Valour.Shared.Villages.VillagePresence presence) =>
        _ = _hub.Clients.Group(Villages.VillagePresenceService.GetGroupId(planetId, mapId))
            .SendAsync("Village-Presence-Joined", presence);

    public void NotifyVillagePresenceMoved(long planetId, long mapId, Valour.Shared.Villages.VillagePresenceMove move) =>
        _ = _hub.Clients.Group(Villages.VillagePresenceService.GetGroupId(planetId, mapId))
            .SendAsync("Village-Presence-Moved", move);

    public void NotifyVillagePresenceLeft(long planetId, long mapId, Valour.Shared.Villages.VillagePresenceLeft left) =>
        _ = _hub.Clients.Group(Villages.VillagePresenceService.GetGroupId(planetId, mapId))
            .SendAsync("Village-Presence-Left", left);

    public void NotifyPlanetItemChange<T>(long planetId, T model, int flags = 0) =>
        SendPlanetItemEvent(planetId, model, $"{typeof(T).Name}-Update", [model, flags], isUpdate: true);

    public void NotifyPlanetItemChange<T>(T model, int flags = 0) where T : ISharedPlanetModel =>
        SendPlanetItemEvent(model.PlanetId, model, $"{typeof(T).Name}-Update", [model, flags], isUpdate: true);

    public void NotifyPlanetItemDelete<T>(T model) where T : ISharedPlanetModel =>
        SendPlanetItemEvent(model.PlanetId, model, $"{typeof(T).Name}-Delete", [model], isUpdate: false);

    public void NotifyPlanetItemDelete<T>(long planetId, T model) =>
        SendPlanetItemEvent(planetId, model, $"{typeof(T).Name}-Delete", [model], isUpdate: false);

    /// <summary>
    /// Planet models that are only readable with a planet permission over HTTP.
    /// Their realtime events go to the members holding that permission instead
    /// of the whole planet group.
    /// </summary>
    private static PlanetPermission GetRequiredPlanetPermission(object model) => model switch
    {
        PlanetReport => PlanetPermissions.ViewReports,
        AutomodTrigger or AutomodAction => PlanetPermissions.Manage,
        PlanetInvite => PlanetPermissions.Invite,
        PlanetBan => PlanetPermissions.Ban,
        _ => null,
    };

    private void SendPlanetItemEvent(long planetId, object model, string method, object[] args, bool isUpdate)
    {
        // Channel updates reveal private channels, so only members who can view
        // the channel receive them.
        if (isUpdate && model is Channel { PlanetId: not null } channel)
        {
            SendToPlanetMembers(planetId, method, args, channelId: channel.Id);
            return;
        }

        var permission = GetRequiredPlanetPermission(model);
        if (permission is not null)
        {
            SendToPlanetMembers(planetId, method, args, permission);
            return;
        }

        _ = _hub.Clients.Group($"p-{planetId}").SendCoreAsync(method, args);
    }

    /// <summary>
    /// Sends an event to the connections in a planet's group whose member passes
    /// the given checks: a planet permission, view access to a channel, or both.
    /// Planet groups live on the planet's host, so the member list and the
    /// permission caches used here are local.
    /// </summary>
    private void SendToPlanetMembers(
        long planetId,
        string method,
        object[] args,
        PlanetPermission permission = null,
        long? channelId = null,
        IReadOnlyCollection<long> excludeUserIds = null)
    {
        var groupId = $"p-{planetId}";
        var members = _connectionTracker.GetGroupMembers(groupId);
        if (members.Length == 0)
            return;

        // Filtering is asynchronous, so sends for one planet are chained to keep
        // their original order. Otherwise a later voice participant list could
        // overtake an earlier one and leave clients with stale state.
        lock (FilteredSendChainLock)
        {
            var previous = FilteredSendChains.GetValueOrDefault(planetId) ?? Task.CompletedTask;
            Task next = null;
            next = RunAfterAsync(previous, async () =>
            {
                await SendToPlanetMembersAsync(
                    planetId, groupId, members, method, args, permission, channelId, excludeUserIds);

                lock (FilteredSendChainLock)
                {
                    if (FilteredSendChains.TryGetValue(planetId, out var latest) && ReferenceEquals(latest, next))
                        FilteredSendChains.Remove(planetId);
                }
            });
            FilteredSendChains[planetId] = next;
        }
    }

    private static readonly object FilteredSendChainLock = new();
    private static readonly Dictionary<long, Task> FilteredSendChains = new();

    private static async Task RunAfterAsync(Task previous, Func<Task> work)
    {
        // Leave the caller (and its lock) before doing any work.
        await Task.Yield();

        // The previous send handles its own failures, so it never faults here.
        await previous;
        await work();
    }

    private async Task SendToPlanetMembersAsync(
        long planetId,
        string groupId,
        (long UserId, long MemberId)[] members,
        string method,
        object[] args,
        PlanetPermission permission,
        long? channelId,
        IReadOnlyCollection<long> excludeUserIds)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var hostedPlanetService = scope.ServiceProvider.GetRequiredService<HostedPlanetService>();
            var permissionService = scope.ServiceProvider.GetRequiredService<PlanetPermissionService>();

            var hosted = (await hostedPlanetService.TryGetAsync(planetId)).HostedPlanet;
            if (hosted is null)
                return;

            HashSet<long> allowed;
            if (channelId is not null)
            {
                var viewers = await permissionService.GetChannelViewerUserIdsAsync(hosted, [channelId.Value]);
                allowed = viewers[0].ToHashSet();
            }
            else
            {
                allowed = new HashSet<long>(members.Length);
                foreach (var member in members)
                    allowed.Add(member.UserId);
            }

            if (permission is not null)
            {
                foreach (var (userId, memberId) in members)
                {
                    if (!allowed.Contains(userId))
                        continue;

                    var hasPermission = hosted.TryGetMember(memberId, out var cachedMember)
                        ? await permissionService.HasPlanetPermissionAsync(cachedMember, permission)
                        : await permissionService.HasPlanetPermissionAsync(memberId, permission);

                    if (!hasPermission)
                        allowed.Remove(userId);
                }
            }

            if (excludeUserIds is not null)
                allowed.ExceptWith(excludeUserIds);

            if (allowed.Count == 0)
                return;

            var connections = new List<string>();
            foreach (var connectionId in _connectionTracker.GetGroupConnections(groupId))
            {
                var token = _connectionTracker.GetToken(connectionId);
                if (token is not null && allowed.Contains(token.UserId))
                    connections.Add(connectionId);
            }

            if (connections.Count > 0)
                await _hub.Clients.Clients(connections).SendCoreAsync(method, args);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send {Method} to permitted members of planet {PlanetId}", method, planetId);
        }
    }

    public void NotifyChannelChange(Channel channel, IReadOnlyList<long> recipientUserIds, int flags = 0)
    {
        if (recipientUserIds.Count == 0)
            return;
        
        var groups = new string[recipientUserIds.Count];
        for (int i = 0; i < recipientUserIds.Count; i++)
            groups[i] = $"u-{recipientUserIds[i]}";

        _ = _hub.Clients.Groups(groups).SendAsync($"{nameof(Channel)}-Update", channel, flags);
    }

    public void NotifyChannelDelete(Channel channel, IReadOnlyList<long> recipientUserIds)
    {
        if (recipientUserIds.Count == 0)
            return;

        var groups = new string[recipientUserIds.Count];
        for (int i = 0; i < recipientUserIds.Count; i++)
            groups[i] = $"u-{recipientUserIds[i]}";

        _ = _hub.Clients.Groups(groups).SendAsync($"{nameof(Channel)}-Delete", channel);
    }

    /// <summary>
    /// Removes any currently-connected clients belonging to the given users from a channel's
    /// real-time message group, so they stop receiving live messages for a channel they've just
    /// lost view access to. A permission change doesn't otherwise affect an already-joined group.
    /// </summary>
    public async Task EvictUsersFromChannelGroupAsync(long channelId, IReadOnlyList<long> userIds)
    {
        if (userIds.Count == 0)
            return;

        var channel = await _db.Channels.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.Id == channelId)
            .Select(x => new { x.PlanetId })
            .FirstOrDefaultAsync();
        if (channel is null)
            return;

        if (channel.PlanetId is null)
        {
            // Direct and group channels are joined through each user's primary
            // node rather than a planet host, so the eviction follows the user.
            foreach (var userId in userIds.Distinct())
            {
                await _nodeLifecycleService.RelayUserEventAsync(
                    userId,
                    NodeLifecycleService.NodeEventType.ChannelRealtimeEviction,
                    new ChannelEvictionPayload(channelId, [userId]));
            }

            return;
        }

        await _nodeLifecycleService.EvictUsersFromChannelRealtimeAsync(
            channel.PlanetId.Value, channelId, userIds);
    }

    /// <summary>
    /// Matches the payload shape <see cref="NodeLifecycleService"/> reads for
    /// <see cref="NodeLifecycleService.NodeEventType.ChannelRealtimeEviction"/>.
    /// </summary>
    private sealed record ChannelEvictionPayload(long ChannelId, long[] UserIds);

    /// <summary>
    /// Removes a former planet member from every realtime group that can
    /// expose that planet's state. Deleting a membership in the database is
    /// not sufficient: SignalR group membership otherwise outlives it and the
    /// connection can continue receiving broadcasts until it reconnects.
    /// </summary>
    public async Task EvictUserFromPlanetRealtimeAsync(long planetId, long userId)
    {
        await _nodeLifecycleService.EvictUserFromPlanetRealtimeAsync(planetId, userId);
    }

    /// <summary>
    /// Executes a planet-membership eviction on the node that owns the
    /// corresponding SignalR groups. Cross-node callers must use
    /// <see cref="EvictUserFromPlanetRealtimeAsync"/> instead.
    /// </summary>
    public async Task EvictUserFromPlanetRealtimeLocalAsync(long planetId, long userId)
    {
        await EvictUsersFromGroupAsync($"p-{planetId}", [userId]);

        var channelIds = await _db.Channels.AsNoTracking()
            .Where(x => x.PlanetId == planetId)
            .Select(x => x.Id)
            .ToListAsync();

        foreach (var channelId in channelIds)
            await EvictUsersFromGroupAsync($"c-{channelId}", [userId]);

        // Village presence uses one group per map.
        var villagePrefix = $"v-{planetId}-";
        foreach (var groupId in _connectionTracker.GetUserGroups(userId))
        {
            if (groupId.StartsWith(villagePrefix, StringComparison.Ordinal))
                await EvictUsersFromGroupAsync(groupId, [userId]);
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var presence = scope.ServiceProvider.GetRequiredService<Villages.VillagePresenceService>();
            if (await presence.LeaveAllAsync(planetId, userId))
            {
                await scope.ServiceProvider.GetRequiredService<Villages.VillageRoomService>()
                    .ReleaseAllForUserAsync(userId, planetId);
            }

            await scope.ServiceProvider.GetRequiredService<VoiceStateService>()
                .ForceRemoveUserAsync(userId, planetId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove user {UserId} from village or voice on planet {PlanetId}",
                userId, planetId);
        }
    }

    /// <summary>
    /// Executes a channel eviction on its hosting node. The public counterpart
    /// first routes to that node through <see cref="NodeLifecycleService"/>.
    /// Users who lose a voice or video channel are also removed from its call.
    /// </summary>
    public async Task EvictUsersFromChannelGroupLocalAsync(long channelId, IReadOnlyList<long> userIds)
    {
        await EvictUsersFromGroupAsync($"c-{channelId}", userIds);

        var planetId = await GetPlanetIdForChannel(channelId);
        if (planetId is null || userIds.Count == 0)
            return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var voiceState = scope.ServiceProvider.GetRequiredService<VoiceStateService>();
            foreach (var userId in userIds)
                await voiceState.ForceRemoveUserAsync(userId, planetId.Value, channelId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove evicted users from voice channel {ChannelId}", channelId);
        }
    }

    private async Task EvictUsersFromGroupAsync(string groupId, IReadOnlyList<long> userIds)
    {
        if (userIds.Count == 0)
            return;

        var connections = _connectionTracker.GetGroupConnections(groupId);
        if (connections.Length == 0)
            return;
        
        for (int i = 0; i < connections.Length; i++)
        {
            var connectionId = connections[i];
            var token = _connectionTracker.GetToken(connectionId);
            if (token is null)
                continue;
            
            bool isRevoked = false;
            for (int j = 0; j < userIds.Count; j++)
            {
                if (userIds[j] == token.UserId)
                {
                    isRevoked = true;
                    break;
                }
            }

            if (!isRevoked)
                continue;

            await _hub.Groups.RemoveFromGroupAsync(connectionId, groupId);
            await _connectionTracker.UntrackGroupMembershipAsync(groupId, connectionId: connectionId);
        }
    }

    public void NotifyPlanetChange(Planet item, int flags = 0) =>
        _ = _hub.Clients.Group($"p-{item.Id}").SendAsync($"{nameof(Planet)}-Update", item, flags);

    public void NotifyPlanetDelete(Planet item) =>
        _ = _hub.Clients.Group($"p-{item.Id}").SendAsync($"{nameof(Planet)}-Delete", item);
    
    /// <summary>
    /// Delivers an embed interaction only to the author of the embed's message.
    /// Interactions can carry form input, which no other member should see.
    /// </summary>
    public void NotifyInteractionEvent(EmbedInteractionEvent interaction) =>
        _ = NotifyInteractionEventAsync(interaction);

    private async Task NotifyInteractionEventAsync(EmbedInteractionEvent interaction)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var authorUserId = await db.PlanetMembers
                .AsNoTracking()
                .Where(x => x.Id == interaction.AuthorMemberId && x.PlanetId == interaction.PlanetId)
                .Select(x => (long?)x.UserId)
                .FirstOrDefaultAsync();

            if (authorUserId is null)
                return;

            await _hub.Clients.Group($"u-{authorUserId.Value}").SendAsync("InteractionEvent", interaction);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deliver embed interaction for message {MessageId}", interaction.MessageId);
        }
    }

    public void NotifyMessageDeletion(Message message) =>
        _ = _hub.Clients.Group($"c-{message.ChannelId}").SendAsync("DeleteMessage", message);

    public void NotifyDirectMessageDeletion(Message message, long targetUserId) =>
        _ = _hub.Clients.Group($"u-{targetUserId}").SendAsync("DeleteMessage", message);

    public void NotifyPersonalEmbedUpdateEvent(EmbedUpdate u) =>
        _ = _hub.Clients.Group($"u-{u.TargetUserId}").SendAsync("Personal-Embed-Update", u);

    public void NotifyChannelEmbedUpdateEvent(EmbedUpdate u) =>
        _ = _hub.Clients.Group($"c-{u.TargetChannelId}").SendAsync("Channel-Embed-Update", u);
    
    public async Task NotifyUserChange(User user, int flags = 0)
    {
        // Write-through: keep the node-global user cache fresh so member reads compose up-to-date
        // user data without re-querying.
        _userCache.Set(user);

        // TODO: Get all locally loaded planets and check if user is member; if so, send update
        // we can probably manage this *without* a database call

        var cutoff = DateTime.UtcNow - PlanetMemberService.OneDayConnectionWindow;
        
        var planetIds = await _db.PlanetMembers
            .AsNoTracking()
            .Where(x => x.UserId == user.Id &&
                        x.TimeLastConnected > cutoff)
            .Select(x => x.PlanetId)
            .ToListAsync();

        foreach (var id in planetIds)
        {
            await _nodeLifecycleService.RelayPlanetUserEventAsync(
                id, NodeLifecycleService.NodeEventType.PlanetUserUpdate, user, flags);
        }
    }

    public async Task NotifyUserDelete(User user)
    {
        _userCache.Remove(user.Id);

        var members = await _db.PlanetMembers.Where(x => x.UserId == user.Id).ToListAsync();

        foreach (var m in members)
        {
            await _nodeLifecycleService.RelayPlanetUserEventAsync(
                m.PlanetId, NodeLifecycleService.NodeEventType.PlanetUserDelete, user);
        }
    }
    
    private async ValueTask<long?> GetPlanetIdForChannel(long channelId)
    {
        if (ChannelToPlanetIdCache.TryGetValue(channelId, out var planetId))
            return planetId;

        var channel = await _db.Channels.Select(x => new { x.Id, x.PlanetId}).FirstOrDefaultAsync(x => x.Id == channelId);
        
        if (channel is null)
            return null;
        
        ChannelToPlanetIdCache[channel.Id] = channel.PlanetId;
        
        return channel.PlanetId;
    }
    
    public async Task UpdateChannelsWatching()
    {
        foreach (var groupId in _connectionTracker.GetAllGroups())
        {
            if (!TryGetChannelGroupId(groupId, out var channelId))
                continue;
            
            var planetId = await GetPlanetIdForChannel(channelId);
            
            if (!planetId.HasValue)
                continue;

            var activeUserIds = await _channelWatchingService.GetActiveViewingUserIdsAsync(channelId);
                
            _ = _hub.Clients.Group(groupId).SendAsync("Channel-Watching-Update", new ChannelWatchingUpdate
            {
                PlanetId = planetId,
                ChannelId = channelId,
                UserIds = activeUserIds.OrderBy(x => x).ToList()
            });
        }
    }

    private static bool TryGetChannelGroupId(string groupId, out long channelId)
    {
        channelId = 0;
        return groupId?.StartsWith("c-") == true &&
               long.TryParse(groupId.AsSpan(2), out channelId);
    }

    public async Task NotifyCurrentlyTyping(long channelId, long userId)
    {
        var planetId = await GetPlanetIdForChannel(channelId);
        
        _ = _hub.Clients.Group($"c-{channelId}").SendAsync("Channel-CurrentlyTyping-Update", new ChannelTypingUpdate
        {
            PlanetId = planetId,
            ChannelId = channelId,
            UserId = userId
        });
    }

    public void NotifyChannelStateUpdate(long planetId, long channelId, DateTime time)
    {
        _ = _hub.Clients.Group($"p-{planetId}").SendAsync("Channel-State", new ChannelStateUpdate(channelId, time, planetId));
    }

    ////////////////
    // Eco Events //
    ////////////////

    /// <summary>
    /// A planet transaction reaches the two users involved and the members who
    /// can manage the planet's eco accounts, not every member of the planet.
    /// </summary>
    public void NotifyPlanetTransactionProcessed(Transaction transaction)
    {
        SendToPlanetMembers(
            transaction.PlanetId,
            "Transaction-Processed",
            [transaction],
            PlanetPermissions.ManageEcoAccounts,
            excludeUserIds: [transaction.UserFromId, transaction.UserToId]);
        _ = _hub.Clients.Group($"u-{transaction.UserFromId}").SendAsync("Transaction-Processed", transaction);
        _ = _hub.Clients.Group($"u-{transaction.UserToId}").SendAsync("Transaction-Processed", transaction);
    }

    public async Task RelayTransaction(Transaction transaction, NodeLifecycleService nodeLifecycleService)
    {
        await nodeLifecycleService.RelayUserEventAsync(transaction.UserFromId, NodeLifecycleService.NodeEventType.Transaction, transaction);
        await nodeLifecycleService.RelayUserEventAsync(transaction.UserToId, NodeLifecycleService.NodeEventType.Transaction, transaction);
    }

    public void NotifyCurrencyChange(Currency item, int flags = 0) =>
        _ =  _hub.Clients.Group($"p-{item.Id}").SendAsync($"Currency-Update", item, flags);
}
