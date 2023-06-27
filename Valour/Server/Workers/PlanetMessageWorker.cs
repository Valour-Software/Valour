using System.Collections.Concurrent;
using Valour.Server.Database;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Server.Workers
{
    public class PlanetMessageWorker : IHostedService, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PlanetMessageWorker> _logger;

        // A queue of all messages that need to be processed
        private static readonly BlockingCollection<PlanetMessage> MessageQueue = new(new ConcurrentQueue<PlanetMessage>());
        
        // A map from channel id to the messages currently queued for that channel
        private static readonly ConcurrentDictionary<long, List<PlanetMessage>> StagedChannelMessages = new();
        
        // A map from message id to the message queued
        private static readonly ConcurrentDictionary<long, PlanetMessage> StagedMessages = new();

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
        
        public static void AddToQueue(PlanetMessage message)
        {
            // Generate Id for message
            message.Id = IdManager.Generate();
            MessageQueue.Add(message);
        }

        public static void RemoveFromQueue(PlanetMessage message)
        {
            // Remove currently staged
            StagedChannelMessages.Remove(message.Id, out _);

            // Protect from being staged
            BlockSet.Add(message.Id);
        }

        public static PlanetMessage GetStagedMessage(long messageId)
        {
            StagedMessages.TryGetValue(messageId, out var staged);
            return staged;
        }
        
        public static List<PlanetMessage> GetStagedMessages(long channelId)
        {
            StagedChannelMessages.TryGetValue(channelId, out var stagedList);
            return stagedList ?? new List<PlanetMessage>();
        }
        
        public Task StartAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting Message Worker");

            // Start the queue task
            _queueTask = Task.Run(ConsumeMessageQueue);
            
            _timer = new Timer(DoWork, null, TimeSpan.Zero, 
                TimeSpan.FromSeconds(20));

            return Task.CompletedTask;
        }

        private async void DoWork(object? state)
        {
            // First check if queue task is running
            if (_queueTask.IsCompleted)
            {
                // If not, restart it
                _queueTask = Task.Run(ConsumeMessageQueue);
                
                _logger.LogInformation($@"Planet Message Worker queue task stopped at: {DateTime.UtcNow}
                                                 Restarting queue task.");
            }
            
            // Don't work if there's no staged messages
            if (!StagedMessages.Any())
                return;

            /* Get required services in new scope */
            await using var scope = _serviceProvider.CreateAsyncScope();
            await using var db = scope.ServiceProvider.GetRequiredService<ValourDB>();
            
            _logger.LogInformation($@"Planet Message Worker running at: {DateTimeOffset.Now.ToString()}
                                             Queue size: {MessageQueue.Count.ToString()}
                                             Saving {StagedMessages.Count.ToString()} messages to DB.");

            var staged = StagedMessages.Values;
            var channelIds = staged.Select(x => x.ChannelId).Distinct();

            /* Update channel last active for all channels where we are saving message update */
            foreach (var channelId in channelIds)
            {
                var lastMessage = StagedChannelMessages[channelId].Last();
                
                var channelState = await db.ChannelStates.FindAsync(channelId);
                if (channelState is null)
                {
                    channelState = new Valour.Database.ChannelState()
                    {
                        ChannelId = channelId,
                        LastUpdateTime = lastMessage.TimeSent,
                        PlanetId = lastMessage.PlanetId,
                    };

                    db.ChannelStates.Add(channelState);
                }
                else
                {
                    channelState.LastUpdateTime = lastMessage.TimeSent;
                    db.ChannelStates.Update(channelState);
                }
            }
            
            await db.PlanetMessages.AddRangeAsync(StagedMessages.Values.Select(x => x.ToDatabase()));
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
            var dbService = scope.ServiceProvider.GetRequiredService<ValourDB>();
            
            // This is a stream and will run forever
            foreach (var message in MessageQueue.GetConsumingEnumerable())
            {
                if (BlockSet.Contains(message.Id))
                    continue; // It's going to get cleared anyways

                message.TimeSent = DateTime.UtcNow;
                
                stateService.SetChannelStateTime(message.ChannelId, message.TimeSent);
                hubService.NotifyChannelStateUpdate(message.PlanetId, message.ChannelId, message.TimeSent);

                PlanetMessage replyTo = null;
                if (message.ReplyToId is not null)
                {
                    replyTo = (await dbService.PlanetMessages.FindAsync(message.ReplyToId)).ToModel();
                }
                
                hubService.RelayMessage(new MessageTransferData<PlanetMessage>()
                {
                    Message = message,
                    Reply = replyTo,
                });

                // Add message to message staging
                StagedMessages[message.Id] = message;

                // Add message to channel-specific staging
                StagedChannelMessages.TryGetValue(message.ChannelId, out var channelStaged);
                if (channelStaged is null)
                {
                    channelStaged = new List<PlanetMessage>();
                    StagedChannelMessages[message.ChannelId] = channelStaged;
                }
                channelStaged.Add(message);
            }
        }
        
        public Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Message Worker is Stopping.");

            _timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }
        
        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
