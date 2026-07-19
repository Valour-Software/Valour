using Valour.Config.Configs;
using Valour.Server.Services;

namespace Valour.Server.Workers;

/// <summary>Keeps discovery stubs, especially member counts, current after local mutations.</summary>
public class FederationPlanetRegistrySyncWorker : IHostedService, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FederationPlanetRegistrySyncWorker> _logger;
    private Timer _timer;
    private int _running;

    public FederationPlanetRegistrySyncWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<FederationPlanetRegistrySyncWorker> logger)
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
        // Avoid overlapping full-registry scans when a hub is slow or down.
        if (Interlocked.Exchange(ref _running, 1) != 0)
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<FederationPlanetRegistrySyncService>().SyncAllAsync();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Federation planet registry reconciliation failed");
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();
}
