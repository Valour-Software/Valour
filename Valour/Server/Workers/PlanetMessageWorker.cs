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

namespace Valour.Server.Workers
{
    public class PlanetMessageWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        public readonly ILogger<MessageCacheWorker> _logger;

        public PlanetMessageWorker(ILogger<MessageCacheWorker> logger,
                            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        private static BlockingCollection<PlanetMessage> MessageQueue =
            new BlockingCollection<PlanetMessage>(new ConcurrentQueue<PlanetMessage>());

        private static ConcurrentBag<PlanetMessage> MessageDBChunk =
            new ConcurrentBag<PlanetMessage>();

        private static ValourDB Context;

        public static void AddToQueue(PlanetMessage message)
        {
            MessageQueue.Add(message);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Task task = Task.Run(async () =>
                {
                    //try
                    //{

                    Context = new ValourDB(ValourDB.DBOptions);

                    // This is a stream and will run forever
                    foreach (PlanetMessage Message in MessageQueue.GetConsumingEnumerable())
                    {
                        ulong channel_id = Message.Channel_Id;

                        PlanetChatChannel channel = await Context.PlanetChatChannels.FindAsync(channel_id);

                        // Get index for message
                        ulong index = channel.Message_Count;

                        // Update message count. May have to queue this in the future to prevent concurrency issues (done).
                        channel.Message_Count += 1;
                        Message.Message_Index = index;

                        string json = JsonConvert.SerializeObject(Message);

                        // This is not awaited on purpose
                        MessageHub.Current.Clients.Group(channel_id.ToString()).SendAsync("Relay", json);

                        MessageDBChunk.Add(Message);
                    }


                    //}
                    //catch (System.Exception e)
                    //{
                    //    Console.WriteLine("FATAL MESSAGE WORKER ERROR:");
                    //    Console.WriteLine(e.Message);
                    //}
                });

                while (!task.IsCompleted)
                {
                    _logger.LogInformation($"Planet Message Worker running at: {DateTimeOffset.Now}");
                    _logger.LogInformation($"Queue size: {MessageQueue.Count}");
                    _logger.LogInformation($"Saving {MessageDBChunk.Count} messages to DB.");

                    if (Context != null)
                    {
                        await Context.PlanetMessages.AddRangeAsync(MessageDBChunk);
                        MessageDBChunk.Clear();
                        await Context.SaveChangesAsync();
                        _logger.LogInformation($"Saved successfully.");
                    }

                    // Save to DB


                    await Task.Delay(1000, stoppingToken);
                }

                _logger.LogInformation("Planet Message Worker task stopped at: {time}", DateTimeOffset.Now);
                _logger.LogInformation("Restarting.", DateTimeOffset.Now);
            }
        }
    }
}
