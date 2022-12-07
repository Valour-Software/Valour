using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using Valour.Server.Database;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Database.Items.Messages;
using Valour.Server.Services;
using Valour.Shared.Channels;
using Valour.Shared.Items.Channels;

namespace Valour.Server.Workers
{
    public class PlanetMessageWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PlanetMessageWorker> _logger;
        

        public PlanetMessageWorker(ILogger<PlanetMessageWorker> logger,
                                   IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        private static BlockingCollection<PlanetMessage> MessageQueue = new(new ConcurrentQueue<PlanetMessage>());

        // Prevents deleted messages from being staged
        private static HashSet<long> BlockSet = new();

        private static ConcurrentDictionary<long, PlanetMessage> StagedMessages = new();

        public static Dictionary<long, long> ChannelMessageIndices = new();

        public static PlanetMessage GetStagedMessage(long id)
        {
            StagedMessages.TryGetValue(id, out PlanetMessage msg);
            return msg;
        }

        public static void AddToQueue(PlanetMessage message)
        {
            MessageQueue.Add(message);
        }

        public static void RemoveFromQueue(PlanetMessage message)
        {
            // Remove currently staged
            StagedMessages.Remove(message.Id, out _);

            // Protect from being staged
            BlockSet.Add(message.Id);
        }

        public static List<PlanetMessage> GetStagedMessages(long channelId, int max)
        {
            return StagedMessages.Values.Where(x => x.ChannelId == channelId).TakeLast(max).ToList();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Task task = Task.Run(async () =>
                {
                    // This is a stream and will run forever
                    foreach (PlanetMessage Message in MessageQueue.GetConsumingEnumerable())
                    {
                        using var scope = _serviceProvider.CreateScope();
                        await using var db = scope.ServiceProvider.GetRequiredService<ValourDB>();
                        var hubService = scope.ServiceProvider.GetRequiredService<CoreHubService>();
                        
                        if (BlockSet.Contains(Message.Id))
                        {
                            BlockSet.Remove(Message.Id);
                            continue;
                        }

                        long channelId = Message.ChannelId;

                        PlanetChatChannel channel = await db.PlanetChatChannels.FindAsync(channelId);

                        // Update message count. May have to queue this in the future to prevent concurrency issues (done).
                        channel.MessageCount += 1;

                        // Get index for message
                        long index = channel.MessageCount;
                        channel.State = $"MessageIndex-{channel.MessageCount}";
                        channel.TimeLastActive = DateTime.UtcNow;

                        Message.MessageIndex = index;
                        Message.TimeSent = DateTime.UtcNow;

                        // This is not awaited on purpose
#pragma warning disable CS4014
                        hubService.NotifyChannelStateUpdate(Message.PlanetId, Message.ChannelId, channel.State);
                        hubService.RelayMessage(Message);
#pragma warning restore CS4014

                        StagedMessages.TryAdd(Message.Id, Message);
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
                    using var scope = _serviceProvider.CreateScope();
                    await using var db = scope.ServiceProvider.GetRequiredService<ValourDB>();
                    
                    _logger.LogInformation($"Planet Message Worker running at: {DateTimeOffset.Now.ToString()}");
                    _logger.LogInformation($"Queue size: {MessageQueue.Count.ToString()}");
                    _logger.LogInformation($"Saving {StagedMessages.Count.ToString()} messages to DB.");

                    if (db != null)
                    {
                        await db.PlanetMessages.AddRangeAsync(StagedMessages.Values);
                        await db.SaveChangesAsync();
                        BlockSet.Clear();
                        StagedMessages.Clear();
                        _logger.LogInformation($"Saved successfully.");
                    }

                    // Save to DB


                    await Task.Delay(30000, stoppingToken);
                }

                _logger.LogInformation("Planet Message Worker task stopped at: {time}", DateTimeOffset.Now.ToString());
                _logger.LogInformation("Restarting.", DateTimeOffset.Now.ToString());
            }
        }
    }
}
