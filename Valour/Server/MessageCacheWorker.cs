using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Valour.Server.Database;

namespace Valour.Server.Workers
{
    public class MessageCacheWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        public readonly ILogger<MessageCacheWorker> _logger;

        public MessageCacheWorker(ILogger<MessageCacheWorker> logger,
                            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Task task = Task.Run(async () =>
                {
                    while (true)
                    {
                        try
                        {
                            using (var scope = _scopeFactory.CreateScope())
                            {
                                ValourDB context = scope.ServiceProvider.GetRequiredService<ValourDB>();
                                DateTime now = DateTime.UtcNow;
                                foreach (Messaging.CacheMessage message in context.Messages)
                                {
                                    if (message.TimeSent.AddHours(24) < now)
                                    {
                                        context.Messages.Remove(message);
                                    }
                                    await context.SaveChangesAsync();
                                }
                            }
                            Console.WriteLine("Checked Message Cache");
                            Thread.Sleep(1000*60*60);
                        }
                        catch(System.Exception e)
                        {
                            Console.WriteLine("FATAL MESSAGE CACHE ERROR:");
                            Console.WriteLine(e.Message);
                        }
                    }
                });

                while (!task.IsCompleted)
                {
                    _logger.LogInformation("Message Cache Worker running at: {time}", DateTimeOffset.Now);
                    await Task.Delay(60000, stoppingToken);
                }

                _logger.LogInformation("Message Cache Worker task stopped at: {time}", DateTimeOffset.Now);
                _logger.LogInformation("Restarting.", DateTimeOffset.Now);
            }
        }
    }
}
