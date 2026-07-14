using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using Valour.Config.Configs;
using Valour.Sdk.Models.Messages.Embeds;
using Valour.Server.Hubs;
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
    private readonly IConnectionMultiplexer _redis;
    private readonly SignalRConnectionService _connectionTracker;
    private readonly ChannelWatchingService _channelWatchingService;
    private readonly UserCacheService _userCache;
    private readonly ILogger<CoreHubService> _logger;

    public CoreHubService(
        ValourDb db,
        IServiceProvider serviceProvider,
        IHubContext<CoreHub> hub,
        IConnectionMultiplexer redis,
        SignalRConnectionService connectionTracker,
        ChannelWatchingService channelWatchingService,
        UserCacheService userCache,
        ILogger<CoreHubService> logger)
    {
        _db = db;
        _hub = hub;
        _serviceProvider = serviceProvider;
        _redis = redis;
        _connectionTracker = connectionTracker;
        _channelWatchingService = channelWatchingService;
        _userCache = userCache;
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
        _ = _hub.Clients.Group($"p-{planetId}").SendAsync("Voice-Channel-Participants", update);

    public void NotifyPlanetItemChange<T>(long planetId, T model, int flags = 0) =>
        _ = _hub.Clients.Group($"p-{planetId}").SendAsync($"{typeof(T).Name}-Update", model, flags);
    
    public async void NotifyPlanetItemChange<T>(T model, int flags = 0) where T : ISharedPlanetModel => 
        await _hub.Clients.Group($"p-{model.PlanetId}").SendAsync($"{typeof(T).Name}-Update", model, flags);

    public void NotifyPlanetItemDelete<T>(T model) where T : ISharedPlanetModel =>
        _ = _hub.Clients.Group($"p-{model.PlanetId}").SendAsync($"{typeof(T).Name}-Delete", model);
    
    public void NotifyPlanetItemDelete<T>(long planetId, T model) =>
        _ = _hub.Clients.Group($"p-{planetId}").SendAsync($"{typeof(T).Name}-Delete", model);

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
        
        var groupId = $"c-{channelId}";
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
    
    public void NotifyInteractionEvent(EmbedInteractionEvent interaction) =>
        _ = _hub.Clients.Group($"i-{interaction.PlanetId}").SendAsync("InteractionEvent", interaction);

    public void NotifyMessageDeletion(Message message) =>
        _ = _hub.Clients.Group($"c-{message.ChannelId}").SendAsync("DeleteMessage", message);

    public void NotifyDirectMessageDeletion(Message message, long targetUserId) =>
        _ = _hub.Clients.Group($"u-{targetUserId}").SendAsync("DeleteMessage", message);

    public void NotifyPersonalEmbedUpdateEvent(PersonalEmbedUpdate u) =>
        _ = _hub.Clients.Group($"u-{u.TargetUserId}").SendAsync("Personal-Embed-Update", u);

    public void NotifyChannelEmbedUpdateEvent(ChannelEmbedUpdate u) =>
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
            // TODO: This will not work with node scaling
            await _hub.Clients.Group($"p-{id}").SendAsync("User-Update", user, flags);
        }
    }

    public async Task NotifyUserDelete(User user)
    {
        _userCache.Remove(user.Id);

        var members = await _db.PlanetMembers.Where(x => x.UserId == user.Id).ToListAsync();

        foreach (var m in members)
        {
            await _hub.Clients.Group($"p-{m.PlanetId}").SendAsync("User-Delete", user);
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

    public void NotifyPlanetTransactionProcessed(Transaction transaction)
    {
        _ = _hub.Clients.Group($"p-{transaction.PlanetId}").SendAsync("Transaction-Processed", transaction);
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
