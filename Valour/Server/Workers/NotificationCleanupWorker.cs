using Valour.Server.Services;

namespace Valour.Server.Workers;

/// <summary>
/// Periodically prunes old, already-read notifications so the notifications
/// table stays bounded. Unread notifications are never touched.
/// </summary>
public class NotificationCleanupWorker : BackgroundService
{
    private readonly ILogger<NotificationCleanupWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan MaxReadAge = TimeSpan.FromDays(30);

    public NotificationCleanupWorker(
        ILogger<NotificationCleanupWorker> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once shortly after startup, then on a fixed interval.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during notification cleanup");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task CleanupAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var deleted = await notificationService.DeleteOldReadNotificationsAsync(MaxReadAge);

        if (deleted > 0)
            _logger.LogInformation("Pruned {Count} read notifications older than {Days} days", deleted, MaxReadAge.TotalDays);
    }
}
