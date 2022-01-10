using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Valour.Database;
using Valour.Database.Items.Messages;
using Valour.Database.Items.Planets.Channels;
using Valour.Shared.Items.Messages;

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

        private static BlockingCollection<PlanetMessage> MessageQueue = new(new ConcurrentQueue<PlanetMessage>());

        private static ConcurrentBag<PlanetMessage> StagedMessages = new();

        private static ValourDB Context;

        public static Dictionary<ulong, ulong> ChannelMessageIndices = new();

        public static void AddToQueue(PlanetMessage message)
        {
            MessageQueue.Add(message);
        }

        public static List<PlanetMessage> GetStagedMessages(ulong channel_id, int max)
        {
            return StagedMessages.Where(x => x.Channel_Id == channel_id).TakeLast(max).Reverse().ToList();
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
                        Message.TimeSent = DateTime.UtcNow;

                        // This is not awaited on purpose
                        PlanetHub.Current.Clients.Group($"c-{channel_id}").SendAsync("Relay", Message);

                        StagedMessages.Add(Message);
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
                    _logger.LogInformation($"Planet Message Worker running at: {DateTimeOffset.Now.ToString()}");
                    _logger.LogInformation($"Queue size: {MessageQueue.Count.ToString()}");
                    _logger.LogInformation($"Saving {StagedMessages.Count.ToString()} messages to DB.");

                    if (Context != null)
                    {
                        await Context.PlanetMessages.AddRangeAsync(StagedMessages);
                        StagedMessages.Clear();
                        await Context.SaveChangesAsync();
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
