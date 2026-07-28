using Microsoft.AspNetCore.SignalR;
using Valour.Server.Hubs;
using Valour.Shared.Models.Staff;

namespace Valour.Server.Workers;

/// <summary>
/// Pushes live platform counters to staff dashboard viewers every five
/// seconds. Skips all computation when this node has no viewers, so the
/// worker is effectively free on nodes nobody is watching from.
/// </summary>
public class DashboardBroadcastWorker : BackgroundService
{
    private readonly ILogger<DashboardBroadcastWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SignalRConnectionService _connectionTracker;
    private readonly IHubContext<CoreHub> _hub;
    private readonly DashboardEventService _dashboardEvents;

    public DashboardBroadcastWorker(
        ILogger<DashboardBroadcastWorker> logger,
        IServiceScopeFactory scopeFactory,
        SignalRConnectionService connectionTracker,
        IHubContext<CoreHub> hub,
        DashboardEventService dashboardEvents)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _connectionTracker = connectionTracker;
        _hub = hub;
        _dashboardEvents = dashboardEvents;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // The presence relay is a passive singleton; starting it here is what
        // guarantees it exists and is subscribed on every node
        await _dashboardEvents.StartAsync();

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

                if (_connectionTracker.GetGroupConnections(DashboardHub.Group).Length == 0)
                    continue;

                using var scope = _scopeFactory.CreateScope();
                var dashboardService = scope.ServiceProvider.GetRequiredService<DashboardService>();

                var stats = await dashboardService.BuildLiveStatsAsync();

                await _hub.Clients.Group(DashboardHub.Group)
                    .SendAsync(DashboardHub.LiveEvent, stats, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting dashboard live stats");
            }
        }
    }
}
