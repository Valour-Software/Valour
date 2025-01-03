namespace Valour.Server.Workers;

/// <summary>
/// Updates the node state in redis every 30 seconds
/// </summary>
public class NodeStateWorker : IHostedService, IDisposable
{
    private readonly ILogger<NodeStateWorker> _logger;
    private NodeLifecycleService _nodeLifecycleService;
    
    // Timer for executing timed tasks
    private Timer _timer;
    
    public NodeStateWorker(
        ILogger<NodeStateWorker> logger,
        NodeLifecycleService nodeLifecycleService)
    {
        _logger = logger;
        _nodeLifecycleService = nodeLifecycleService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _nodeLifecycleService.StartAsync();
        
        _timer = new Timer(UpdateNodeState, null, TimeSpan.Zero,
            TimeSpan.FromSeconds(30));
    }
    
    private void UpdateNodeState(object state)
    {
        _logger.LogInformation("Updating node state");
        _ = _nodeLifecycleService.UpdateNodeAliveAsync();
    }
    
    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Node State Worker is Stopping");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }
        
    public void Dispose()
    {
        _timer?.Dispose();
    }
}