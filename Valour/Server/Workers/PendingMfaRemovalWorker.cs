namespace Valour.Server.Workers;

/// <summary>
/// Executes staff-scheduled MFA removals once their safety delay has
/// passed. The delay (see StaffService.MfaRemovalDelay) plus the notice
/// email give the real account owner time to cancel a removal they did
/// not ask for.
/// </summary>
public class PendingMfaRemovalWorker : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingMfaRemovalWorker> _logger;
    private Timer _timer;
    private int _isRunning;

    public PendingMfaRemovalWorker(ILogger<PendingMfaRemovalWorker> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public Task StartAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Pending MFA Removal Worker");

        _timer = new Timer(DoWork, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));

        return Task.CompletedTask;
    }

    private async void DoWork(object state)
    {
        // Skip if a previous run is still going
        if (Interlocked.Exchange(ref _isRunning, 1) == 1)
            return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var staffService = scope.ServiceProvider.GetRequiredService<StaffService>();

            var executed = await staffService.ExecutePendingMfaRemovalsAsync();
            if (executed > 0)
                _logger.LogInformation("Executed {Count} scheduled MFA removals", executed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing pending MFA removals");
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }

    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Stopping Pending MFA Removal Worker");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
