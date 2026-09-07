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
        var channelService = scope.ServiceProvider.GetRequiredService<ChannelService>();
        
        // Perform startup tasks
        var startupService = scope.ServiceProvider.GetRequiredService<StartupService>();
        await startupService.EnsureVictorAndValourCentralReady();
        await startupService.EnsureBootstrapAdminAsync();

        // Generate the federation signing key on first run (hub mode)
        var federationKeys = scope.ServiceProvider.GetRequiredService<FederationKeyService>();
        await federationKeys.EnsureKeysAsync();
        
        // Ensure all default roles are positioned correctly
        var defRowsUpdated = await db.PlanetRoles.Where(x => x.IsDefault)
            .ExecuteUpdateAsync(x => x.SetProperty(x => x.Position, uint.MaxValue));
        
        _logger.LogInformation("Updated {Count} default roles", defRowsUpdated);
        
        // Create profiles for bots that are missing them
        var botsWithoutProfiles = await db.Users
            .Where(x => x.Bot && !db.UserProfiles.Any(p => p.Id == x.Id))
            .ToListAsync();

        foreach (var bot in botsWithoutProfiles)
        {
            db.UserProfiles.Add(new Valour.Database.UserProfile
            {
                Id = bot.Id,
                Headline = "Bot Account",
                Bio = $"I'm {bot.Name}, a bot on Valour!",
                BorderColor = "#fff",
                AnimatedBorder = false,
            });
        }

        if (botsWithoutProfiles.Count > 0)
        {
            await db.SaveChangesAsync();
            _logger.LogInformation("Created profiles for {Count} bots", botsWithoutProfiles.Count);
        }

        // Clean up members who have active bans but are still members (due to previous bug)
        var activeBans = await db.PlanetBans
            .Where(x => x.TimeExpires == null || x.TimeExpires > DateTime.UtcNow)
            .Select(x => new { x.TargetId, x.PlanetId })
            .ToListAsync();

        var bannedMembersRemoved = 0;
        foreach (var ban in activeBans)
        {
            var member = await db.PlanetMembers
                .FirstOrDefaultAsync(x => x.UserId == ban.TargetId && x.PlanetId == ban.PlanetId && !x.IsDeleted);

            if (member is not null)
            {
                member.IsDeleted = true;
                bannedMembersRemoved++;
            }
        }

        if (bannedMembersRemoved > 0)
        {
            await db.SaveChangesAsync();
            _logger.LogInformation("Removed {Count} members who had active bans", bannedMembersRemoved);
        }

        _logger.LogInformation("Migration Worker has finished");

        // Migrate channels
        await channelService.MigrateChannels();

        var associatedChatsBackfilled = await channelService.BackfillAssociatedCallChatsAsync();
        if (associatedChatsBackfilled > 0)
        {
            _logger.LogInformation(
                "Backfilled associated chat channels for {Count} call channels",
                associatedChatsBackfilled);
        }
    }
    
    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Migration Worker is stopping");
        return Task.CompletedTask;
    }
    
}
