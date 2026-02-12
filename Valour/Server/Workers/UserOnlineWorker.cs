namespace Valour.Server.Workers;

public class UserOnlineWorker : BackgroundService
{
    private const int MaxBatchItems = 2000;
    private static readonly TimeSpan TickDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _serviceProvider;
    private readonly UserOnlineQueueService _onlineQueue;
    private readonly ILogger<UserOnlineWorker> _logger;

    public UserOnlineWorker(
        IServiceProvider serviceProvider,
        UserOnlineQueueService onlineQueue,
        ILogger<UserOnlineWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _onlineQueue = onlineQueue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TickDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var updates = _onlineQueue.Drain(MaxBatchItems);
            if (updates.Count == 0)
                continue;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var onlineService = scope.ServiceProvider.GetRequiredService<UserOnlineService>();
                await onlineService.UpdateOnlineStatesBatchAsync(updates, stoppingToken);
            }
            catch (Exception ex)
            {
                _onlineQueue.Requeue(updates);
                _logger.LogWarning(ex,
                    "Failed processing online state batch with {Count} updates. Requeued.",
                    updates.Count);
            }
        }
    }
}
