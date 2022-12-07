using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Valour.Api.Items.Messages;
using Valour.Api.Items.Messages.Embeds;
using Valour.Server.Database;
using Valour.Server.Database.Items.Channels;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Users;
using Valour.Server.Hubs;
using Valour.Shared.Channels;
using Valour.Shared.Items.Channels;
using DirectMessage = Valour.Server.Database.Items.Messages.DirectMessage;
using PlanetMessage = Valour.Server.Database.Items.Messages.PlanetMessage;

namespace Valour.Server.Services;

public class CoreHubService
{
    // Map of channelids to users typing from prev channel update
    public static ConcurrentDictionary<long, List<long>> PrevCurrentlyTyping = new ConcurrentDictionary<long, List<long>>();
    
    private readonly IHubContext<CoreHub> _hub;
    private readonly ValourDB _db;
    private readonly IServiceProvider _serviceProvider;
    
    public CoreHubService(ValourDB db, IServiceProvider serviceProvider, IHubContext<CoreHub> hub)
    {
        _db = db;
        _hub = hub;
        _serviceProvider = serviceProvider;
    }
    
    public async void RelayMessage(PlanetMessage message)
    {
        var groupId = $"c-{message.ChannelId}";

        // Group we are sending messages to
        var group = _hub.Clients.Group(groupId);

        if (ConnectionTracker.GroupConnections.ContainsKey(groupId)) {
            // All of the connections to this group
            var viewingIds = ConnectionTracker.GroupUserIds[groupId];
            
            await _db.Database.ExecuteSqlRawAsync("CALL batch_user_channel_state_update({0}, {1}, {2});", 
                viewingIds, message.ChannelId, ChannelStateService.GetState(message.ChannelId));
        }

        await group.SendAsync("Relay", message);
    }
    
    public async void RelayDirectMessage(DirectMessage message, long targetUserId)
    {
        var groupId = $"u-{targetUserId}";

        // Group we are sending messages to
        var group = _hub.Clients.Group(groupId);
        
        await group.SendAsync("RelayDirect", message);
    }
    
    public async void NotifyUserChannelStateUpdate(long userId, UserChannelState state) =>
        await _hub.Clients.Group($"u-{userId}").SendAsync("UserChannelState-Update", state);

    public async void NotifyPlanetItemChange(IPlanetItem item, int flags = 0) =>
        await _hub.Clients.Group($"p-{item.PlanetId}").SendAsync($"{item.GetType().Name}-Update", item, flags);

    public async void NotifyPlanetItemDelete(IPlanetItem item) =>
        await _hub.Clients.Group($"p-{item.PlanetId}").SendAsync($"{item.GetType().Name}-Delete", item);

    public async void NotifyPlanetChange(Planet item, int flags = 0) =>
        await _hub.Clients.Group($"p-{item.Id}").SendAsync($"{item.GetType().Name}-Update", item, flags);

    public async void NotifyPlanetDelete(Planet item) =>
        await _hub.Clients.Group($"p-{item.Id}").SendAsync($"{item.GetType().Name}-Delete", item);
    
    public async void NotifyInteractionEvent(EmbedInteractionEvent interaction) =>
        await _hub.Clients.Group($"i-{interaction.PlanetId}").SendAsync("InteractionEvent", interaction);

    public async void NotifyMessageDeletion(PlanetMessage message) =>
        await _hub.Clients.Group($"c-{message.ChannelId}").SendAsync("DeleteMessage", message);

    public async void NotifyDirectMessageDeletion(DirectMessage message, long targetUserId) =>
        await _hub.Clients.Group($"u-{targetUserId}").SendAsync("DeleteMessage", message);

    public async void NotifyPersonalEmbedUpdateEvent(PersonalEmbedUpdate u) =>
        await _hub.Clients.Group($"u-{u.TargetUserId}").SendAsync("Personal-Embed-Update", u);

    public async void NotifyChannelEmbedUpdateEvent(ChannelEmbedUpdate u) =>
        await _hub.Clients.Group($"c-{u.TargetChannelId}").SendAsync("Channel-Embed-Update", u);
    
    public async Task NotifyUserChange(User user, int flags = 0)
    {
        var members = await _db.PlanetMembers.Where(x => x.UserId == user.Id).ToListAsync();

        foreach (var m in members)
        {
            // TODO: This will not work with node scaling
            await _hub.Clients.Group($"p-{m.PlanetId}").SendAsync("User-Update", user, flags);
        }
    }

    public async Task NotifyUserDelete(User user)
    {
        var members = await _db.PlanetMembers.Where(x => x.UserId == user.Id).ToListAsync();

        foreach (var m in members)
        {
            await _hub.Clients.Group($"p-{m.PlanetId}").SendAsync("User-Delete", user);
        }
    }
    
    public async void UpdateChannelsWatching()
        {
            foreach (var pair in ConnectionTracker.GroupUserIds)
            {
                // Channel connections only
                if (!pair.Key.StartsWith('c'))
                    continue;

                // Send current active channel connection user ids

                // no need to await these
#pragma warning disable CS4014
                var channelid = long.Parse(pair.Key.Substring(2));
                _hub.Clients.Group(pair.Key).SendAsync("Channel-Watching-Update", new ChannelWatchingUpdate
                {
                    ChannelId = channelid,
                    UserIds = pair.Value.Distinct().ToList()
                });
#pragma warning restore CS4014
            }
        }

        public async void NotifyCurrentlyTyping(long channelId, long userId)
        {
            await _hub.Clients.Group($"c-{channelId}").SendAsync("Channel-CurrentlyTyping-Update", new ChannelTypingUpdate
            {
                ChannelId = channelId,
                UserId = userId
            });
        }

        public async void NotifyChannelStateUpdate(long planetId, long channelId, string state)
        {
            await _hub.Clients.Group($"p-{planetId}").SendAsync("Channel-State", new ChannelStateUpdate(channelId, state));
        }

}