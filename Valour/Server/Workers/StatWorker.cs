using Valour.Server.Database;

namespace Valour.Server.Workers;

public class StatWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    public readonly ILogger<StatWorker> _logger;
    private static int _messageCount = 0;

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Task task = Task.Run(async () =>
            {
                //try
                //{

                using (var scope = _scopeFactory.CreateScope())
                {
                    ValourDB context = scope.ServiceProvider.GetRequiredService<ValourDB>();

                    if (System.Diagnostics.Debugger.IsAttached == false)
                    {
                        StatObject stats = new();
                        stats.TimeCreated = DateTime.UtcNow;
                        stats.UserCount = await context.Users.CountAsync();
                        stats.PlanetCount = await context.Planets.CountAsync();
                        stats.PlanetMemberCount = await context.PlanetMembers.CountAsync();
                        stats.ChannelCount = await context.PlanetChatChannels.CountAsync();
                        stats.CategoryCount = await context.PlanetCategoryChannels.CountAsync();
                        stats.MessageDayCount = await context.PlanetMessages.CountAsync();
                        stats.MessagesSent = _messageCount;
                        _messageCount = 0;
                        await context.Stats.AddAsync(stats);
                        await context.SaveChangesAsync();
                        stats = new StatObject();
                        _logger.LogInformation($"Saved successfully.");
                    }
                }
            });
            while (!task.IsCompleted)
            {
                _logger.LogInformation($"Stat Worker running at: {DateTimeOffset.Now.ToString()}");
                await Task.Delay(60000, stoppingToken);
            }

            _logger.LogInformation("Stat Worker task stopped at: {time}", DateTimeOffset.Now.ToString());
            _logger.LogInformation("Restarting.", DateTimeOffset.Now.ToString());
        }
    }
}
