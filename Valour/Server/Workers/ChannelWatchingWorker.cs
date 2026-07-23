namespace Valour.Server.Workers;

public class ChannelWatchingWorker : BackgroundService
{
    private readonly ILogger<ChannelWatchingWorker> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ChannelWatchingWorker(ILogger<ChannelWatchingWorker> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        var i = 0;

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var hubService = scope.ServiceProvider.GetRequiredService<CoreHubService>();
                    
                    await hubService.UpdateChannelsWatching();

                    i++;

                    if (i % 5 == 0)
                    {
                        _logger.LogInformation("Channel Watching Worker running at {Time}", DateTimeOffset.Now.ToString());
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Channel Watching Worker failed to update channel watching state");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        _logger.LogInformation("Channel Watching task stopped at {Time}", DateTimeOffset.Now.ToString());
    }
}
