using System.Collections.Concurrent;

namespace Valour.Server.Workers
{
    public class PlanetMessageWorker : IHostedService, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PlanetMessageWorker> _logger;

        // A queue of all messages that need to be processed
        private static readonly BlockingCollection<Message> MessageQueue = new(new ConcurrentQueue<Message>());
        
        // A map from channel id to the messages currently queued for that channel
        private static readonly ConcurrentDictionary<long, List<Message>> StagedChannelMessages = new();
        
        // A map from message id to the message queued
        private static readonly ConcurrentDictionary<long, Message> StagedMessages = new();

        // Prevents deleted messages from being staged
        private static readonly HashSet<long> BlockSet = new();

        /// <summary>
        /// Holds the long-running queue task
        /// </summary>
        private static Task _queueTask;
        
        // Timer for executing timed tasks
        private Timer _timer;
        
        public PlanetMessageWorker(ILogger<PlanetMessageWorker> logger,
                                   IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }
        
        public static void AddToQueue(Message message)
        {
            if (message.PlanetId is null)
            {
                Console.WriteLine("[!!!] Tried to add a message to the planet message queue queue without a planet id");
                return;
            }

            // Generate Id for message
            MessageQueue.Add(message);
        }

        public static void RemoveFromQueue(Message message)
        {
            // Remove currently staged
            StagedChannelMessages.Remove(message.Id, out _);

            // Protect from being staged
            BlockSet.Add(message.Id);
        }

        public static Message GetStagedMessage(long messageId)
        {
            StagedMessages.TryGetValue(messageId, out var staged);
            return staged;
        }
        
        public static List<Message> GetStagedMessages(long channelId)
        {
            StagedChannelMessages.TryGetValue(channelId, out var stagedList);
            return stagedList ?? new List<Message>();
        }
        
        public Task StartAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting Message Worker");

            // Start the queue task
            _queueTask = Task.Run(ConsumeMessageQueue, stoppingToken);
            
            _timer = new Timer(DoWork, null, TimeSpan.Zero, 
                TimeSpan.FromSeconds(20));

            return Task.CompletedTask;
        }

        private async void DoWork(object state)
        {
            // First check if queue task is running
            if (_queueTask.IsCompleted)
            {
                // If not, restart it
                _queueTask = Task.Run(ConsumeMessageQueue);
                
                _logger.LogInformation("Planet Message Worker queue task stopped at: {Time}, Restarting queue task", DateTime.UtcNow);
            }
            
            // Don't work if there's no staged messages
            if (!StagedMessages.Any())
                return;

            /* Get required services in new scope */
            await using var scope = _serviceProvider.CreateAsyncScope();
            await using var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            
            _logger.LogInformation(@"Planet Message Worker running at: {Time}
                                             Queue size: {QueueSize}
                                             Saving {StagedCount} messages to DB", DateTimeOffset.Now, MessageQueue.Count, StagedMessages.Count);

            List<long> cleanup = null;
            int stagedMessages = 0;
            
            /* Update channel last active for all channels where we are saving message update */
            foreach (var channelMessages in StagedChannelMessages)
            {
                stagedMessages += channelMessages.Value.Count;
                
                // If there are no messages to be posted to a channel, we can remove the staging list
                // and save memory
                if (channelMessages.Value.Count == 0)
                {
                    if (cleanup is null)
                        cleanup = new List<long>(){ channelMessages.Key };
                    else
                        cleanup.Add(channelMessages.Key);
                }
            }

            var messages = new List<Valour.Database.Message>(stagedMessages);
            foreach (var message in StagedMessages.Values)
            {
                messages.Add(message.ToDatabase());
            }
            
            // Perform cleanup
            if (cleanup is not null)
            {
                foreach (var channelId in cleanup)
                {
                    StagedChannelMessages.Remove(channelId, out _);
                }
            }
            
            await db.Messages.AddRangeAsync(messages);
            await db.SaveChangesAsync();
            BlockSet.Clear();
            StagedMessages.Clear();
            StagedChannelMessages.Clear();
            _logger.LogInformation($"Saved successfully.");
            
        }

        /// <summary>
        /// This task should run forever and consume messages from
        /// the queue.
        /// </summary>
        private async Task ConsumeMessageQueue()
        {
            // This scope is long-living, which is usually bad. But it's only used to send events,
            // and does not insert into the database, so it should be fine.
            await using var scope = _serviceProvider.CreateAsyncScope();
            var hubService = scope.ServiceProvider.GetRequiredService<CoreHubService>();
            var stateService = scope.ServiceProvider.GetRequiredService<ChannelStateService>();
            
            // This is ONLY READ FROM
            var dbService = scope.ServiceProvider.GetRequiredService<ValourDb>();
            
            // This is a stream and will run forever
            foreach (var message in MessageQueue.GetConsumingEnumerable())
            {
                if (BlockSet.Contains(message.Id))
                    continue; // It's going to get cleared anyways

                message.TimeSent = DateTime.UtcNow;
                
                stateService.SetChannelStateTime(message.ChannelId, message.TimeSent);
                hubService.NotifyChannelStateUpdate(message.PlanetId!.Value, message.ChannelId, message.TimeSent);
                
                if (message.ReplyToId is not null && message.ReplyTo is null)
                {
                    var replyTo = (await dbService.Messages.FindAsync(message.ReplyToId)).ToModel();
                    message.ReplyTo = replyTo;
                }
                
                await hubService.RelayMessage(message);

                // Add message to message staging
                StagedMessages[message.Id] = message;

                // Add message to channel-specific staging
                StagedChannelMessages.TryGetValue(message.ChannelId, out var channelStaged);
                if (channelStaged is null)
                {
                    channelStaged = new List<Message>();
                    StagedChannelMessages[message.ChannelId] = channelStaged;
                }
                channelStaged.Add(message);
            }
        }
        
        public Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Message Worker is Stopping");

            _timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }
        
        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
