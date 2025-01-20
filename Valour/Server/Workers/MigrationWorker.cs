namespace Valour.Server.Workers;

/// <summary>
/// Migrates from one version to the next!
/// </summary>
public class MigrationWorker : IHostedService
{
    private readonly ILogger<MigrationWorker> _logger;
    private IServiceScopeFactory _scopeFactory;
    
    public MigrationWorker(
        ILogger<MigrationWorker> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Migration Worker is starting");
        
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var permService = scope.ServiceProvider.GetRequiredService<PlanetPermissionService>();
        var channelService = scope.ServiceProvider.GetRequiredService<ChannelService>();
        
        // Perform startup tasks
        var startupService = scope.ServiceProvider.GetRequiredService<StartupService>();
        await startupService.EnsureVictorAndValourCentralReady();
        
        // Generate role hash keys for all members
        await permService.BulkUpdateMemberRoleHashesAsync();
        _logger.LogInformation("Migration Worker has finished");

        // Migrate channels
        await channelService.MigrateChannels();
    }
    
    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Migration Worker is stopping");
        return Task.CompletedTask;
    }
    
}