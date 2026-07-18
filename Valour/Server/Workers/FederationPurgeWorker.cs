using Valour.Config.Configs;
using Valour.Server.Services;

namespace Valour.Server.Workers;

/// <summary>
/// On community nodes, periodically pulls account-deletion tombstones from the
/// hub and purges those users' local shadow data. No-op on non-node instances.
/// </summary>
public class FederationPurgeWorker : IHostedService, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Lookback = TimeSpan.FromHours(25); // overlap the interval generously

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FederationPurgeWorker> _logger;
    private Timer _timer;

    public FederationPurgeWorker(IServiceScopeFactory scopeFactory, ILogger<FederationPurgeWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (FederationConfig.Current?.NodeEnabled != true)
            return Task.CompletedTask;

        _timer = new Timer(async _ => await RunAsync(), null, TimeSpan.FromMinutes(1), Interval);
        return Task.CompletedTask;
    }

    private async Task RunAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var purge = scope.ServiceProvider.GetRequiredService<FederationPurgeService>();
            await purge.HonorPurgesAsync(Lookback);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Federation purge poll failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();
}
