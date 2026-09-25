using Valour.Database;
using Valour.Shared.Models;

namespace Valour.Server.Workers;

public class StatWorker : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StatWorker> _logger;
    private Timer _timer;
    private static int _messageCount;
    private int _isRunning;

    // The totals come from full-table counts, which are expensive on large
    // tables. Each minute row reuses the last totals, and the counts are only
    // repeated at this interval.
    private static readonly TimeSpan TotalsRefreshInterval = TimeSpan.FromMinutes(15);
    private StatObject _totals;
    private DateTime _totalsTime;

    public StatWorker(ILogger<StatWorker> logger,
                        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public static void IncreaseMessageCount()
    {
        Interlocked.Increment(ref _messageCount);
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
        if (Interlocked.Exchange(ref _isRunning, 1) == 1)
            return;

        // Timer callbacks are async void, so an unhandled exception here would
        // terminate the process
        try
        {
            if (System.Diagnostics.Debugger.IsAttached)
                return;

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ValourDb>();

            var now = DateTime.UtcNow;
            if (_totals is null || now - _totalsTime >= TotalsRefreshInterval)
            {
                _totals = new StatObject
                {
                    UserCount = await context.Users.CountAsync(),
                    PlanetCount = await context.Planets.CountAsync(),
                    PlanetMemberCount = await context.PlanetMembers.CountAsync(),
                    ChannelCount = await context.Channels.CountAsync(x => x.ChannelType == ChannelTypeEnum.PlanetChat),
                    CategoryCount = await context.Channels.CountAsync(x => x.ChannelType == ChannelTypeEnum.PlanetCategory),
                    MessageDayCount = await context.Messages.CountAsync(),
                };
                _totalsTime = now;
            }

            var stats = new StatObject
            {
                TimeCreated = now,
                UserCount = _totals.UserCount,
                PlanetCount = _totals.PlanetCount,
                PlanetMemberCount = _totals.PlanetMemberCount,
                ChannelCount = _totals.ChannelCount,
                CategoryCount = _totals.CategoryCount,
                MessageDayCount = _totals.MessageDayCount,
                MessagesSent = Interlocked.Exchange(ref _messageCount, 0),
            };

            await context.Stats.AddAsync(stats);
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stat Worker failed to save stats");
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
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
