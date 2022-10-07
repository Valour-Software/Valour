using Valour.Server.Database;

namespace Valour.Server.Workers;

public class ChannelWatchingWorker : BackgroundService
{
    public readonly ILogger<StatWorker> _logger;

    public ChannelWatchingWorker(ILogger<StatWorker> logger)
    {
        _logger = logger;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Task task = Task.Run(async () => {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(5000);
                    await PlanetHub.UpdateChannelsWatching();
                }
            });

            while (!task.IsCompleted)
            {
                _logger.LogInformation($"Channel Watching Worker running at: {DateTimeOffset.Now.ToString()}");
                await Task.Delay(30000, stoppingToken);
            }

            _logger.LogInformation("Channel Watching task stopped at: {time}", DateTimeOffset.Now.ToString());
            _logger.LogInformation("Restarting.", DateTimeOffset.Now.ToString());
        }
    }
}
