using System.Collections.Concurrent;
using Valour.Shared.Models;

namespace Valour.Server.Workers
{
    public class PlanetMessageWorker : IHostedService, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PlanetMessageWorker> _logger;

        // A queue of all messages that need to be processed
        private static readonly BlockingCollection<Message> MessageQueue = new(new ConcurrentQueue<Message>());
        
        // A map from channel id to the messages currently queued for that channel
        private static readonly ConcurrentDictionary<long, ConcurrentQueue<Message>> StagedChannelMessages = new();
        
        // A map from message id to the message queued
        private static readonly ConcurrentDictionary<long, Message> StagedMessages = new();

        // Messages that have been accepted for queueing but not yet consumed
        private static readonly ConcurrentDictionary<long, Message> QueuedMessages = new();

        // Prevents deleted messages from being staged
        private static readonly ConcurrentDictionary<long, byte> BlockSet = new();

        /// <summary>
        /// Holds the long-running queue task
        /// </summary>
        private static Task _queueTask;
        
        // Timer for executing timed tasks
        private Timer _timer;
        private int _isFlushing;
        
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

            QueuedMessages[message.Id] = message;
            MessageQueue.Add(message);
        }

        public static void RemoveFromQueue(Message message)
        {
            var wasStaged = StagedMessages.TryRemove(message.Id, out _);
            if (wasStaged)
            {
                RemoveStagedMessageFromChannel(message);
            }

            var wasQueued = QueuedMessages.TryRemove(message.Id, out _);

            // If a message was already staged, there is nothing left to block.
            // Otherwise we mark it blocked so the consumer skips it if it is still queued
            // or in-flight between dequeue and staging.
            if (wasQueued || !wasStaged)
            {
                BlockSet[message.Id] = 0;
            }
            else
            {
                BlockSet.TryRemove(message.Id, out _);
            }
        }

        public static Message GetStagedMessage(long messageId)
        {
            StagedMessages.TryGetValue(messageId, out var staged);
            return staged;
        }

        public static Message GetQueuedMessage(long messageId)
        {
            QueuedMessages.TryGetValue(messageId, out var queued);
            return queued;
        }
        
        public static List<Message> GetStagedMessages(long channelId)
        {
            if (StagedChannelMessages.TryGetValue(channelId, out var stagedQueue))
                return stagedQueue.ToList();

            return new List<Message>();
        }

        public static List<Message> MarkAttachmentMissing(string cdnBucketItemId, string fileName)
        {
            var changedStagedMessages = new List<Message>();

            foreach (var message in StagedMessages.Values)
            {
                if (MarkAttachmentMissing(message, cdnBucketItemId, fileName))
                    changedStagedMessages.Add(message);
            }

            foreach (var message in QueuedMessages.Values)
            {
                MarkAttachmentMissing(message, cdnBucketItemId, fileName);
            }

            return changedStagedMessages;
        }

        private static bool MarkAttachmentMissing(Message message, string cdnBucketItemId, string fileName)
        {
            if (message.Attachments is null)
                return false;

            var changed = false;
            foreach (var attachment in message.Attachments.Where(x => x.CdnBucketItemId == cdnBucketItemId))
            {
                attachment.CdnBucketItemId = null;
                attachment.Location = Valour.Sdk.Models.MessageAttachment.MissingLocation;
                attachment.Type = MessageAttachmentType.File;
                attachment.MimeType = "application/octet-stream";
                attachment.FileName = fileName;
                attachment.Width = 0;
                attachment.Height = 0;
                attachment.Inline = false;
                attachment.Missing = true;
                attachment.Data = null;
                attachment.OpenGraph = null;
                changed = true;
            }

            if (changed)
                message.SetAttachments(message.Attachments);

            return changed;
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
            if (Interlocked.Exchange(ref _isFlushing, 1) == 1)
                return;

            try
            {
            // First check if queue task is running
            if (_queueTask.IsCompleted)
            {
                // If not, restart it
                _queueTask = Task.Run(ConsumeMessageQueue);
                
                _logger.LogInformation("Planet Message Worker queue task stopped at: {Time}, Restarting queue task", DateTime.UtcNow);
            }
            
            // Don't work if there's no staged messages
            if (StagedMessages.IsEmpty)
                return;

            /* Get required services in new scope */
            await using var scope = _serviceProvider.CreateAsyncScope();
            await using var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            
            _logger.LogInformation(@"Planet Message Worker running at: {Time}
                                             Queue size: {QueueSize}
                                             Saving {StagedCount} messages to DB", DateTimeOffset.Now, MessageQueue.Count, StagedMessages.Count);

            var stagedSnapshot = StagedMessages.Values.ToArray();
            var messages = new List<Valour.Database.Message>(stagedSnapshot.Length);
            foreach (var message in stagedSnapshot)
            {
                messages.Add(message.ToDatabase());
            }

            if (messages.Count == 0)
                return;

            try
            {
                await db.Messages.AddRangeAsync(messages);
                await db.SaveChangesAsync();
            } 
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to save messages to database. Falling back to per-message save.");
                
                // If we fail to save all messages at once, we can try to save them one by one
                // We will just dump any messages that fail to save
                
                foreach (var message in messages)
                {
                    try
                    {
                        await db.Messages.AddAsync(message);
                        await db.SaveChangesAsync();
                        StagedMessages.TryRemove(message.Id, out _);
                        RemoveStagedMessageFromChannel(message.ChannelId, message.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to save message {MessageId} to database. Skipping.", message.Id);
                    }
                }
            }
            foreach (var staged in stagedSnapshot)
            {
                StagedMessages.TryRemove(staged.Id, out _);
                RemoveStagedMessageFromChannel(staged);
            }
            _logger.LogInformation($"Saved successfully.");
            }
            finally
            {
                Volatile.Write(ref _isFlushing, 0);
            }
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

            // This is ONLY READ FROM
            var dbService = scope.ServiceProvider.GetRequiredService<ValourDb>();
            
            // This is a stream and will run forever
            foreach (var message in MessageQueue.GetConsumingEnumerable())
            {
                QueuedMessages.TryRemove(message.Id, out _);

                if (BlockSet.ContainsKey(message.Id))
                {
                    BlockSet.TryRemove(message.Id, out _);
                    continue;
                }

                message.TimeSent = DateTime.UtcNow;
                
                hubService.NotifyChannelStateUpdate(message.PlanetId!.Value, message.ChannelId, message.TimeSent);
                
                if (message.ReplyToId is not null && message.ReplyTo is null)
                {
                    var replyToDb = await dbService.Messages
                        .AsNoTracking()
                        .Include(x => x.Attachments)
                        .Include(x => x.Mentions)
                        .FirstOrDefaultAsync(x => x.Id == message.ReplyToId);
                    if (replyToDb is not null)
                    {
                        message.ReplyTo = replyToDb.ToModel();
                    }
                    else
                    {
                        // The referenced reply-to message no longer exists (deleted).
                        // Clear ReplyToId to avoid FK constraint violation (fk_replyto) on insert.
                        message.ReplyToId = null;
                    }
                }
                
                hubService.RelayMessage(message);

                // Add message to message staging
                StagedMessages[message.Id] = message;

                // Add message to channel-specific staging
                var channelStaged = StagedChannelMessages.GetOrAdd(message.ChannelId, _ => new ConcurrentQueue<Message>());
                channelStaged.Enqueue(message);
            }
        }

        private static void RemoveStagedMessageFromChannel(Message message)
            => RemoveStagedMessageFromChannel(message.ChannelId, message.Id);

        private static void RemoveStagedMessageFromChannel(long channelId, long messageId)
        {
            if (!StagedChannelMessages.TryGetValue(channelId, out var stagedQueue))
                return;

            var remaining = stagedQueue.Where(m => m.Id != messageId).ToArray();
            if (remaining.Length == 0)
            {
                StagedChannelMessages.TryRemove(channelId, out _);
            }
            else
            {
                StagedChannelMessages[channelId] = new ConcurrentQueue<Message>(remaining);
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
