namespace Valour.Server.Workers;

public class ChannelWatchingWorker : BackgroundService
{
    private readonly ILogger<StatWorker> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ChannelWatchingWorker(ILogger<StatWorker> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Task task = Task.Run(async () => {
                while (!stoppingToken.IsCancellationRequested)
                {
                    using var scope = _serviceProvider.CreateScope();
                    var hubService = scope.ServiceProvider.GetRequiredService<CoreHubService>();
                    
                    await Task.Delay(5000);
                    hubService.UpdateChannelsWatching();
                }
            });

            while (!task.IsCompleted)
            {
                _logger.LogInformation($"Channel Watching Worker running at: {DateTimeOffset.Now.ToString()}");
                await Task.Delay(30000, stoppingToken);
            }

            _logger.LogInformation("Channel Watching task stopped at: {time}", DateTimeOffset.Now.ToString());
            _logger.LogInformation("Restarting. {time}", DateTimeOffset.Now.ToString());
        }
    }
}
