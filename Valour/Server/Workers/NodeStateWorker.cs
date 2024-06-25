using StackExchange.Redis;
using Valour.Database;
using Valour.Server.Cdn;
using Valour.Server.Cdn.Objects;
using Valour.Server.Database;
using Valour.Shared.Models;
using Valour.Shared.Models.Economy;

namespace Valour.Server.Workers;

/// <summary>
/// Updates the node state in redis every 30 seconds
/// </summary>
public class NodeStateWorker : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NodeStateWorker> _logger;
    private NodeService _longNodeService; // This can ONLY be used for redis operations. Database-dependent
                                                   // operations will cause scoping issues
    
    
    // Timer for executing timed tasks
    private Timer _timer;
    
    public NodeStateWorker(ILogger<NodeStateWorker> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        _longNodeService = scope.ServiceProvider.GetRequiredService<NodeService>();
        
        // Migrate to new access system
        
        /*
        var db = scope.ServiceProvider.GetRequiredService<ValourDB>();
        var accessService = scope.ServiceProvider.GetRequiredService<ChannelAccessService>();
        
        Console.WriteLine("Migrating to channel access system...");

        int c = 0;

        var allBaseRoles = await db.PlanetRoles.Where(x => x.Position == int.MaxValue).Select(x => x.Id).ToListAsync();
        
        foreach (var id in allBaseRoles)
        {
            await accessService.UpdateAllChannelAccessForMembersInRole(id);
            
            if (c % 20 == 0)
                Console.WriteLine($"Done {c} / {allBaseRoles.Count}");
            
            c++;
        }

        Console.WriteLine("Finished migrate");
        
        */
        
        
        await _longNodeService.AnnounceNode();
        
        _timer = new Timer(UpdateNodeState, null, TimeSpan.Zero,
            TimeSpan.FromSeconds(30));
    }
    
    private async void UpdateNodeState(object state)
    {
        _logger.LogInformation("Updating Node State.");
        await _longNodeService.UpdateNodeAliveAsync();
    }
    
    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Node State Worker is Stopping.");

        _timer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }
        
    public void Dispose()
    {
        _timer?.Dispose();
    }
}