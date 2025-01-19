using Valour.Database;
using Valour.Shared.Models;

namespace Valour.Server.Workers;

public class StatWorker : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StatWorker> _logger;
    private Timer _timer;
    private static int _messageCount;

    public StatWorker(ILogger<StatWorker> logger,
                        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public static void IncreaseMessageCount()
    {
        _messageCount += 1;
    }
    
    public Task StartAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Stat Worker");

        _timer = new Timer(DoWork, null, TimeSpan.Zero, 
            TimeSpan.FromSeconds(60));

        return Task.CompletedTask;
    }

    private async void DoWork(object state)
    {
        using var scope = _scopeFactory.CreateScope();
            
        ValourDb context = scope.ServiceProvider.GetRequiredService<ValourDb>();

        if (!System.Diagnostics.Debugger.IsAttached)
        {
            StatObject stats = new();
            stats.TimeCreated = DateTime.UtcNow;

            stats.UserCount = await context.Users.CountAsync();
            stats.PlanetCount = await context.Planets.CountAsync();
            stats.PlanetMemberCount = await context.PlanetMembers.CountAsync();
            stats.ChannelCount = await context.Channels.CountAsync(x => x.ChannelType == ChannelTypeEnum.PlanetChat);
            stats.CategoryCount = await context.Channels.CountAsync(x => x.ChannelType == ChannelTypeEnum.PlanetCategory);
            stats.MessageDayCount = await context.Messages.CountAsync();
            stats.MessagesSent = _messageCount;
            _messageCount = 0;
            
            await context.Stats.AddAsync(stats);
            await context.SaveChangesAsync();
            _logger.LogInformation($"Saved Stats Successfully");
        }
            
        _logger.LogInformation("Stat Worker running at: {Time}", DateTimeOffset.Now.ToString());
    }

    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Stat Worker is Stopping");

        _timer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
