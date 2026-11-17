using Valour.Config.Configs;
using Valour.Server.Services.GoogleDrive;

namespace Valour.Server.Workers;

/// <summary>
/// Copies attachments to their owners' Google Drives and removes Valour's copy
/// once it has expired. See <see cref="DriveSyncService"/>.
/// </summary>
public class DriveSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DriveSyncWorker> _logger;

    public DriveSyncWorker(IServiceScopeFactory scopeFactory, ILogger<DriveSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the server finish starting before the first pass.
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(5, GoogleDriveConfig.Current.SyncIntervalSeconds));

            if (!UserDriveService.IsAvailable)
            {
                // Nothing to do on a server without Google Drive. Check again in case
                // the settings change.
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                continue;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var sync = scope.ServiceProvider.GetRequiredService<DriveSyncService>();

                // Keep going while a pass fills the whole batch, so a backlog clears
                // without waiting for the timer between every batch.
                int copied;
                do
                {
                    copied = await sync.RunPassAsync(stoppingToken);
                }
                while (copied >= GoogleDriveConfig.Current.SyncBatchSize && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Google Drive sync pass failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
