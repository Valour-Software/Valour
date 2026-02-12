namespace Valour.Server.Workers;

public class SubscriptionWorker : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SubscriptionWorker> _logger;


    // Timer for executing timed tasks
    private Timer _timer;
    private int _isRunning;
    
    public SubscriptionWorker(ILogger<SubscriptionWorker> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Update hourly. Probably reduce this when there's many nodes.
        _timer = new Timer(UpdateSubscriptions, null, TimeSpan.Zero,
            TimeSpan.FromHours(1));

        return Task.CompletedTask;
    }
    
    private async void UpdateSubscriptions(object state)
    {
        if (Interlocked.Exchange(ref _isRunning, 1) == 1)
            return;

        try
        {
        using var scope = _serviceProvider.CreateScope();
        
        var service = scope.ServiceProvider.GetRequiredService<SubscriptionService>();
        
        // get users who have now expired
        await service.ProcessActiveDue();
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
    }
    
    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Subscription Worker is Stopping");

        _timer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }
        
    public void Dispose()
    {
        _timer?.Dispose();
    }
}
