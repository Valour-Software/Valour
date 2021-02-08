using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Valour.Server.Database;
using Valour.Shared.Messages;
using System.Collections.Concurrent;
using Valour.Shared.Channels;
using Newtonsoft.Json;
using Valour.Server.Messages;
using Microsoft.AspNetCore.SignalR;
using Valour.Shared;
using System.Collections.Generic;
using System.Linq;
using Valour.Server.Planets;
using Valour.Server.Database;

namespace Valour.Server.Workers
{
    public class StatWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        public readonly ILogger<StatWorker> _logger;

        public StatWorker(ILogger<StatWorker> logger,
                            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        private static ValourDB Context;

        private static StatObject stats = new StatObject();

        public static void IncreaseMessageCount()
        {
            stats.MessagesSent += 1;
        }

        public static IHostingEnvironment env;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Task task = Task.Run(async () =>
                {
                    //try
                    //{

                    Context = new ValourDB(ValourDB.DBOptions);

                    if (Context != null && System.Diagnostics.Debugger.IsAttached == false)
                    {
                        stats.Time = DateTime.UtcNow;
                        stats.UserCount = Context.Users.Count();
                        stats.PlanetCount = Context.Planets.Count();
                        stats.PlanetMemberCount = Context.PlanetMembers.Count();
                        stats.ChannelCount = Context.PlanetChatChannels.Count();
                        stats.CategoryCount = Context.PlanetCategories.Count();
                        stats.Message24hCount = Context.PlanetMessages.Count();
                        await Context.Stats.AddAsync(stats);
                        await Context.SaveChangesAsync(); 
                        stats = new StatObject();
                        _logger.LogInformation($"Saved successfully.");
                    }
                });
                while (!task.IsCompleted)
                {
                    _logger.LogInformation($"Stat Worker running at: {DateTimeOffset.Now}");
                    await Task.Delay(60000, stoppingToken);
                }

                _logger.LogInformation("Stat Worker task stopped at: {time}", DateTimeOffset.Now);
                _logger.LogInformation("Restarting.", DateTimeOffset.Now);
            }
        }
    }
}
