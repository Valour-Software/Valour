using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using Valour.Server.Hubs;
using Valour.Shared.Models.Staff;

namespace Valour.Server.Services;

/// <summary>
/// Fans user online/offline transitions out to staff dashboard viewers.
/// Every node publishes its local transitions to a shared Redis channel and
/// every node subscribes to it (including its own messages), so viewers see
/// cluster-wide presence no matter which node they are connected to.
/// User resolution is skipped entirely when this node has no viewers.
/// </summary>
public class DashboardEventService
{
    private readonly ISubscriber _redisChannel;
    private readonly SignalRConnectionService _connectionTracker;
    private readonly IHubContext<CoreHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DashboardEventService> _logger;

    private readonly RedisChannel _presenceChannel =
        new("dashboard-presence", RedisChannel.PatternMode.Literal);

    private sealed class PresenceTransition
    {
        public long UserId { get; init; }
        public bool Online { get; init; }
        public DateTime TimeUtc { get; init; }
    }

    public DashboardEventService(
        IConnectionMultiplexer redis,
        SignalRConnectionService connectionTracker,
        IHubContext<CoreHub> hub,
        IServiceScopeFactory scopeFactory,
        ILogger<DashboardEventService> logger)
    {
        _redisChannel = redis.GetSubscriber();
        _connectionTracker = connectionTracker;
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync()
    {
        (await _redisChannel.SubscribeAsync(_presenceChannel))
            .OnMessage(OnPresenceMessageAsync);

        _connectionTracker.PrimaryPresenceChanged += OnLocalPresenceChanged;
    }

    private void OnLocalPresenceChanged(long userId, bool online)
    {
        try
        {
            var json = JsonSerializer.Serialize(new PresenceTransition
            {
                UserId = userId,
                Online = online,
                TimeUtc = DateTime.UtcNow,
            });

            _ = _redisChannel.PublishAsync(_presenceChannel, json, CommandFlags.FireAndForget);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to publish dashboard presence transition");
        }
    }

    private async Task OnPresenceMessageAsync(ChannelMessage channelMessage)
    {
        try
        {
            // No local viewers means no reason to resolve anything
            if (_connectionTracker.GetGroupConnections(DashboardHub.Group).Length == 0)
                return;

            var transition = JsonSerializer.Deserialize<PresenceTransition>(channelMessage.Message.ToString());
            if (transition is null)
                return;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();

            var name = await db.Users.AsNoTracking()
                .Where(x => x.Id == transition.UserId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync();

            if (name is null)
                return;

            var planetId = await db.PlanetMembers.AsNoTracking()
                .Where(x => x.UserId == transition.UserId)
                .Select(x => (long?)x.PlanetId)
                .FirstOrDefaultAsync();

            var presenceEvent = new DashboardPresenceEvent
            {
                UserId = transition.UserId,
                Name = name,
                Online = transition.Online,
                PlanetId = planetId,
                TimeUtc = transition.TimeUtc,
            };

            await _hub.Clients.Group(DashboardHub.Group)
                .SendAsync(DashboardHub.PresenceEvent, presenceEvent);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error handling dashboard presence message");
        }
    }
}
