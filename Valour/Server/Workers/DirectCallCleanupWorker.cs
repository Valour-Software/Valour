namespace Valour.Server.Workers;

public sealed class DirectCallCleanupWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DirectCallCleanupWorker> _logger;

    public DirectCallCleanupWorker(IServiceProvider services, ILogger<DirectCallCleanupWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DirectCallService>().ExpireRingingCallsAsync();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to expire missed direct calls");
            }
        }
    }
}
