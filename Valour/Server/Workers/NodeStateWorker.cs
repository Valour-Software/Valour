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

        /*
        var db = scope.ServiceProvider.GetRequiredService<ValourDB>();
        foreach (var account in await db.EcoAccounts.IgnoreQueryFilters().Where(x => x.PlanetMemberId == null).ToListAsync())
        {
            if (account.PlanetMemberId is not null)
                continue;

            var member = await db.PlanetMembers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.PlanetId == account.PlanetId && x.UserId == account.UserId);
            if (member is null)
                continue;

            account.PlanetMemberId = member.Id;

            await db.SaveChangesAsync();

            Console.WriteLine($"Migrated account {account.Id}");
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