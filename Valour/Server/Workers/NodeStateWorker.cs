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

        // Temporary code for migrating CDN items

        var db = scope.ServiceProvider.GetRequiredService<ValourDB>();
        var cdbDb = scope.ServiceProvider.GetRequiredService<CdnDb>();

        if (db.CdnProxyItems.Count() == 0)
        {
            var trans = await db.Database.BeginTransactionAsync();

            try
            {
                // Copy across all proxy items
                List<CdnProxyItem> toAdd = new();
                
                foreach (var item in await cdbDb.ProxyItems.ToListAsync())
                {
                    var proxyItem = new CdnProxyItem()
                    {
                        Id = item.Id,
                        Origin = item.Origin,
                        MimeType = item.MimeType,
                        Width = item.Width,
                        Height = item.Height
                    };

                    toAdd.Add(proxyItem);
                }

                await db.CdnProxyItems.AddRangeAsync(toAdd);
                
                _logger.LogInformation($"Migrating {toAdd.Count()} proxy items");
                
                // Copy across images and content
                
                List<CdnBucketItem> bucketItemsToAdd = new();
                
                var bucketItems = await cdbDb.BucketItems.ToListAsync();
                
                foreach (var item in bucketItems)
                {
                    var bucketItem = new CdnBucketItem()
                    {
                        Id = item.Id,
                        Category = (Valour.Database.ContentCategory)((int)item.Category),
                        Hash = item.Hash,
                        MimeType = item.MimeType,
                        UserId = item.UserId,
                        FileName = item.FileName
                    };

                    bucketItemsToAdd.Add(bucketItem);
                }
                
                await db.CdnBucketItems.AddRangeAsync(bucketItemsToAdd);
                
                _logger.LogInformation($"Migrating {bucketItemsToAdd.Count()} bucket items");
                
                await db.SaveChangesAsync();

                await trans.CommitAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error migrating proxy items");
                await trans.RollbackAsync();
                return;
            }
        }

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
    
    private async void UpdateNodeState(object? state)
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