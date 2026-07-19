using Valour.Config.Configs;
using Valour.Server.Services;

namespace Valour.Server.Workers;

/// <summary>Retries offline federation invite receipts after hub recovery.</summary>
public class FederationInviteReconciliationWorker : IHostedService, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FederationInviteReconciliationWorker> _logger;
    private Timer _timer;
    private int _running;

    public FederationInviteReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<FederationInviteReconciliationWorker> logger)
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
        // Avoid concurrent receipt deliveries: a timer callback does not wait
        // for a prior async invocation to finish.
        if (Interlocked.Exchange(ref _running, 1) != 0)
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<FederationInviteReconciliationService>().SyncPendingAsync();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Federation invite reconciliation failed");
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
