using Valour.Server.Database;

namespace Valour.Server.Workers;

public class ChannelCurrentlyTypingWorker : BackgroundService
{
    public readonly ILogger<StatWorker> _logger;

    public ChannelCurrentlyTypingWorker(ILogger<StatWorker> logger)
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
                    await PlanetHub.UpdateCurrentlyTypingChannels();
                }
            });

            while (!task.IsCompleted)
            {
                _logger.LogInformation($"Channel Currently Typing Worker running at: {DateTimeOffset.Now.ToString()}");
                await Task.Delay(30000, stoppingToken);
            }

            _logger.LogInformation("Channel Currently Typing task stopped at: {time}", DateTimeOffset.Now.ToString());
            _logger.LogInformation("Restarting.", DateTimeOffset.Now.ToString());
        }
    }
}
