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

            var i = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = _serviceProvider.CreateScope();
                var hubService = scope.ServiceProvider.GetRequiredService<CoreHubService>();
                
                await hubService.UpdateChannelsWatching();
                
                await Task.Delay(5000);
                i++;

                if (i % 5 == 0)
                {
                    _logger.LogInformation("Channel Watching Worker running at {Time}", DateTimeOffset.Now.ToString());
                }
            }

            _logger.LogInformation("Channel Watching task stopped at {Time}", DateTimeOffset.Now.ToString());
            _logger.LogInformation("Restarting at {Time}", DateTimeOffset.Now.ToString());
        }
    }
}
