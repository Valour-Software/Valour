using System.Collections.Concurrent;
using Valour.Server.Workers;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// The chat cache service handles caching things related to chats.
/// The caches are shared by every request on this node. A channel's entry is dropped once it
/// has not been read or written for <see cref="IdleTimeout"/>, and the number of cached
/// channels is capped by <see cref="MaxCachedChannels"/>.
/// </summary>
public class ChatCacheService
{
    private const int CacheCapacity = 50;
    private const int MaxCachedChannels = 2048;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Cached last messages in the channel, oldest first
    /// </summary>
    private static readonly ChannelCacheStore<Message> LastMessagesCaches = new(x => x.Id);

    /// <summary>
    /// Cached last chatters in the channel, least recent first
    /// </summary>
    private static readonly ChannelCacheStore<PlanetMember> LastChattersCaches = new(x => x.Id);

    private static readonly Timer SweepTimer = new(_ => Sweep(), null, SweepInterval, SweepInterval);

    private readonly ValourDb _db;

    public ChatCacheService(ValourDb db)
    {
        _db = db;
    }

    private static void Sweep()
    {
        try
        {
            var idleCutoff = Environment.TickCount64 - (long)IdleTimeout.TotalMilliseconds;
            LastMessagesCaches.Evict(idleCutoff, MaxCachedChannels);
            LastChattersCaches.Evict(idleCutoff, MaxCachedChannels);
        }
        catch
        {
            // A failed sweep leaves entries for the next one; it must not crash the timer thread.
        }
    }

    private Task<List<Message>> GetCachedMessagesAsync(long channelId)
    {
        return LastMessagesCaches.GetOrLoadAsync(channelId, async () =>
        {
            var messages = await _db.Messages.AsSplitQuery()
                .AsNoTracking()
                .Include(x => x.ReplyToMessage)
                    .ThenInclude(x => x.Attachments)
                .Include(x => x.ReplyToMessage)
                    .ThenInclude(x => x.Mentions)
                .Include(x => x.ReplyToMessage)
                    .ThenInclude(x => x.Reactions)
                .Include(x => x.Reactions)
                .Include(x => x.Attachments)
                .Include(x => x.Mentions)
                .Where(m => m.ChannelId == channelId)
                .OrderByDescending(m => m.Id)
                .Take(CacheCapacity)
                .Reverse() // Reverse the order to get the oldest messages first
                .Select(x => x.ToModel())
                .ToArrayAsync();

            // Planet messages that are accepted but not yet flushed to the database
            var staged = PlanetMessageWorker.GetStagedMessages(channelId);

            return messages.Concat(staged).ToList();
        }, MergeLoadedMessages);
    }

    /// <summary>
    /// Combines loaded messages with messages that changed while the load ran. Messages
    /// added during the load are the live instances, so they win over loaded copies.
    /// </summary>
    private static List<Message> MergeLoadedMessages(List<Message> loaded, LoadChanges<Message> changes)
    {
        var byId = new Dictionary<long, Message>(loaded.Count + changes.Added.Count);
        foreach (var message in loaded)
            byId[message.Id] = message;
        foreach (var message in changes.Added)
            byId[message.Id] = message;

        foreach (var removedId in changes.RemovedIds)
            byId.Remove(removedId);

        foreach (var replacement in changes.Replaced.Values)
        {
            if (byId.ContainsKey(replacement.Id))
                byId[replacement.Id] = replacement;
        }

        var result = byId.Values
            .OrderBy(x => x.Id)
            .TakeLast(CacheCapacity)
            .ToList();

        foreach (var replacement in changes.Replaced.Values)
        {
            foreach (var message in result)
            {
                if (message.ReplyToId == replacement.Id)
                    message.ReplyTo = replacement;
            }
        }

        return result;
    }

    public void AddMessage(Message message)
    {
        if (LastMessagesCaches.TryGet(message.ChannelId, out var cache))
            cache.Add(message);
    }

    public void RemoveMessage(long channelId, long messageId)
    {
        if (LastMessagesCaches.TryGet(channelId, out var cache))
            cache.Remove(messageId);
    }

    public void ReplaceMessage(Message message)
    {
        if (LastMessagesCaches.TryGet(message.ChannelId, out var cache))
        {
            cache.Replace(message, items =>
            {
                foreach (var cached in items)
                {
                    if (cached.ReplyToId == message.Id)
                        cached.ReplyTo = message;
                }
            });
        }
    }

    public void MarkAttachmentMissing(string cdnBucketItemId, string fileName)
    {
        foreach (var cache in LastMessagesCaches.All())
        {
            cache.Update(messages =>
            {
                var channelChanged = false;

                foreach (var message in messages)
                {
                    if (message.Attachments is null)
                        continue;

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
                    {
                        message.SetAttachments(message.Attachments);
                        channelChanged = true;
                    }
                }

                return channelChanged;
            });
        }
    }

    public Task<List<Message>> GetLastMessagesAsync(long channelId)
        => GetCachedMessagesAsync(channelId);

    public Task<List<PlanetMember>> GetCachedChatPlanetMembersAsync(long channelId)
    {
        return LastChattersCaches.GetOrLoadAsync(channelId, async () =>
        {
            // Get the last 50 messages' authors, newest first
            var members = await _db.Messages
                .AsNoTracking()
                .Include(m => m.AuthorMember)
                .ThenInclude(me => me.User)
                .Where(m => m.ChannelId == channelId && m.AuthorMemberId != null)
                .OrderByDescending(m => m.Id)
                .Take(CacheCapacity)
                .Select(m => m.AuthorMember.ToModel())
                .ToArrayAsync();

            // Store least recent first so the least recent chatter is dropped at capacity
            var result = new List<PlanetMember>(members.Length);
            var added = new HashSet<long>();
            for (int i = members.Length - 1; i >= 0; i--)
            {
                var member = members[i];
                if (member is not null && added.Add(member.Id))
                    result.Add(member);
            }

            return result;
        }, MergeLoadedChatters);
    }

    /// <summary>
    /// Combines loaded chatters with chatters that changed while the load ran. Chatters added
    /// during the load are the most recent, so they move to the end.
    /// </summary>
    private static List<PlanetMember> MergeLoadedChatters(List<PlanetMember> loaded, LoadChanges<PlanetMember> changes)
    {
        var addedIds = changes.Added.Select(x => x.Id).ToHashSet();

        var result = new List<PlanetMember>(loaded.Count + changes.Added.Count);
        foreach (var member in loaded)
        {
            if (!addedIds.Contains(member.Id))
                result.Add(member);
        }
        result.AddRange(changes.Added);

        result.RemoveAll(x => changes.RemovedIds.Contains(x.Id));

        for (int i = 0; i < result.Count; i++)
        {
            if (changes.Replaced.TryGetValue(result[i].Id, out var replacement))
                result[i] = replacement;
        }

        if (result.Count > CacheCapacity)
            result.RemoveRange(0, result.Count - CacheCapacity);

        return result;
    }

    public void AddChatPlanetMember(long channelId, PlanetMember member)
    {
        if (member is null)
            return;

        if (LastChattersCaches.TryGet(channelId, out var cache))
        {
            // Remove any existing member with the same ID, then add as most recent
            cache.Add(member, removeExisting: true);
        }
    }

    public void RemoveChatPlanetMember(long channelId, long memberId)
    {
        if (LastChattersCaches.TryGet(channelId, out var cache))
            cache.Remove(memberId);
    }

    public void ReplaceChatPlanetMember(long channelId, PlanetMember member)
    {
        if (LastChattersCaches.TryGet(channelId, out var cache))
            cache.Replace(member);
    }

    /// <summary>
    /// Changes applied to a channel cache while its initial load was running.
    /// </summary>
    private sealed class LoadChanges<T>
    {
        public List<T> Added { get; } = new();
        public HashSet<long> RemovedIds { get; } = new();
        public Dictionary<long, T> Replaced { get; } = new();
    }

    /// <summary>
    /// The newest items of one channel, oldest first, capped at <see cref="CacheCapacity"/>.
    /// All state is guarded by one lock. Readers receive a snapshot list that is built at
    /// most once per change and never modified afterwards; the items in it are shared.
    /// </summary>
    private sealed class ChannelCache<T> where T : class
    {
        private readonly Lock _sync = new();
        private readonly Func<T, long> _getId;
        private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private List<T> _items = new();
        private List<T> _snapshot;
        private LoadChanges<T> _loadChanges = new();
        private long _lastAccess = Environment.TickCount64;

        public ChannelCache(Func<T, long> getId)
        {
            _getId = getId;
        }

        public Task Loaded => _loaded.Task;

        public long LastAccess => Volatile.Read(ref _lastAccess);

        public void Touch() => Volatile.Write(ref _lastAccess, Environment.TickCount64);

        public void CompleteLoad(List<T> loaded, Func<List<T>, LoadChanges<T>, List<T>> merge)
        {
            lock (_sync)
            {
                _items = merge(loaded, _loadChanges);
                _loadChanges = null;
                _snapshot = null;
            }

            _loaded.TrySetResult();
        }

        public void FailLoad(Exception e)
        {
            _loaded.TrySetException(e);

            // Mark the exception observed; callers receive it through their own await
            _ = _loaded.Task.Exception;
        }

        public List<T> GetSnapshot()
        {
            lock (_sync)
            {
                return _snapshot ??= new List<T>(_items);
            }
        }

        public void Add(T item, bool removeExisting = false)
        {
            lock (_sync)
            {
                var id = _getId(item);
                if (removeExisting)
                    _items.RemoveAll(x => _getId(x) == id);

                _items.Add(item);
                if (_items.Count > CacheCapacity)
                    _items.RemoveRange(0, _items.Count - CacheCapacity);

                if (_loadChanges is not null)
                {
                    if (removeExisting)
                        _loadChanges.Added.RemoveAll(x => _getId(x) == id);
                    _loadChanges.Added.Add(item);
                    _loadChanges.RemovedIds.Remove(id);
                }

                _snapshot = null;
            }

            Touch();
        }

        public void Remove(long id)
        {
            lock (_sync)
            {
                _items.RemoveAll(x => _getId(x) == id);

                if (_loadChanges is not null)
                {
                    _loadChanges.Added.RemoveAll(x => _getId(x) == id);
                    _loadChanges.Replaced.Remove(id);
                    _loadChanges.RemovedIds.Add(id);
                }

                _snapshot = null;
            }
        }

        public void Replace(T item, Action<List<T>> afterReplace = null)
        {
            lock (_sync)
            {
                var id = _getId(item);
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_getId(_items[i]) == id)
                        _items[i] = item;
                }

                afterReplace?.Invoke(_items);

                if (_loadChanges is not null)
                {
                    for (int i = 0; i < _loadChanges.Added.Count; i++)
                    {
                        if (_getId(_loadChanges.Added[i]) == id)
                            _loadChanges.Added[i] = item;
                    }

                    _loadChanges.Replaced[id] = item;
                }

                _snapshot = null;
            }
        }

        /// <summary>
        /// Runs an in-place change on the items. The change returns true if it modified anything.
        /// </summary>
        public void Update(Func<List<T>, bool> change)
        {
            lock (_sync)
            {
                if (change(_items))
                    _snapshot = null;
            }
        }
    }

    private sealed class ChannelCacheStore<T> where T : class
    {
        private readonly ConcurrentDictionary<long, ChannelCache<T>> _caches = new();
        private readonly Func<T, long> _getId;

        public ChannelCacheStore(Func<T, long> getId)
        {
            _getId = getId;
        }

        public bool TryGet(long channelId, out ChannelCache<T> cache) =>
            _caches.TryGetValue(channelId, out cache);

        public IEnumerable<ChannelCache<T>> All() => _caches.Values;

        /// <summary>
        /// Returns the channel's snapshot, loading it first if the channel is not cached.
        /// Concurrent first reads share one load. The load runs on the DbContext captured by
        /// the request that started it, and only that request's load uses the context while
        /// the request awaits it. A failed load is removed so the next read retries.
        /// </summary>
        public async Task<List<T>> GetOrLoadAsync(
            long channelId,
            Func<Task<List<T>>> load,
            Func<List<T>, LoadChanges<T>, List<T>> merge)
        {
            var created = new ChannelCache<T>(_getId);
            var cache = _caches.GetOrAdd(channelId, created);
            cache.Touch();

            if (ReferenceEquals(cache, created))
            {
                try
                {
                    cache.CompleteLoad(await load(), merge);
                }
                catch (Exception e)
                {
                    _caches.TryRemove(new KeyValuePair<long, ChannelCache<T>>(channelId, cache));
                    cache.FailLoad(e);
                    throw;
                }
            }
            else
            {
                await cache.Loaded;
            }

            return cache.GetSnapshot();
        }

        /// <summary>
        /// Removes loaded entries idle since before <paramref name="idleCutoff"/>, then the
        /// least recently used entries while more than <paramref name="maxEntries"/> remain.
        /// </summary>
        public void Evict(long idleCutoff, int maxEntries)
        {
            foreach (var pair in _caches)
            {
                if (pair.Value.Loaded.IsCompleted && pair.Value.LastAccess < idleCutoff)
                    _caches.TryRemove(pair);
            }

            var excess = _caches.Count - maxEntries;
            if (excess <= 0)
                return;

            var oldest = _caches
                .Where(x => x.Value.Loaded.IsCompleted)
                .OrderBy(x => x.Value.LastAccess)
                .Take(excess)
                .ToList();

            foreach (var pair in oldest)
                _caches.TryRemove(pair);
        }
    }
}
