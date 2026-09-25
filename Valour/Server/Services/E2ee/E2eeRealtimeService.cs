using Microsoft.AspNetCore.SignalR;
using Valour.Sdk.E2ee;
using Valour.Server.Hubs;

namespace Valour.Server.Services;

/// <summary>
/// Sends end-to-end encryption notifications to users and channel viewers.
/// </summary>
public class E2eeRealtimeService
{
    private readonly IHubContext<CoreHub> _hub;
    private readonly NodeLifecycleService _nodeLifecycleService;

    public E2eeRealtimeService(IHubContext<CoreHub> hub, NodeLifecycleService nodeLifecycleService)
    {
        _hub = hub;
        _nodeLifecycleService = nodeLifecycleService;
    }

    public Task SendToUserAsync(long userId, E2eeRealtimeEvent evt) =>
        _nodeLifecycleService.RelayUserEventAsync(userId, NodeLifecycleService.NodeEventType.E2ee, evt);

    /// <summary>
    /// Sends to everyone currently watching a channel. Planet channels use this
    /// because it works on whichever node hosts the planet.
    /// </summary>
    public Task SendToChannelAsync(long channelId, E2eeRealtimeEvent evt) =>
        _hub.Clients.Group($"c-{channelId}").SendAsync(E2eeRealtimeEvent.HubMethod, evt);
}
