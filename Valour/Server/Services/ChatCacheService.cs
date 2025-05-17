using System.Collections.Concurrent;
using MessageReaction = Valour.Database.MessageReaction;

namespace Valour.Server.Services;

/// <summary>
/// The chat cache service handles caching things related to chats
/// </summary>
public class ChatCacheService
{
    /// <summary>
    /// Cached last messages in the channel
    /// </summary>
    private static readonly ConcurrentDictionary<long, ConcurrentCircularBuffer<Message>> _lastMessagesCaches
        = new ();

    private static readonly ConcurrentDictionary<long, List<Message>> _lastMessageCacheSnapshots 
        = new ();
    
    /// <summary>
    /// Cached last chatters in the channel
    /// </summary>
    private static readonly ConcurrentDictionary<long, ConcurrentCircularBuffer<PlanetMember>> _lastChattersCaches
        = new ();
    
    private static readonly ConcurrentDictionary<long, List<PlanetMember>> _lastChattersCacheSnapshots
        = new ();
    
    private readonly ValourDb _db;
    
    public ChatCacheService(ValourDb db)
    {
        _db = db;
    }
    
    private async Task<List<Message>> GetCachedMessagesAsync(long channelId)
    {
        var created = false;
        if (!_lastMessagesCaches.TryGetValue(channelId, out var cache))
        {
            cache = new ConcurrentCircularBuffer<Message>(50);
            _lastMessagesCaches[channelId] = cache;
            created = true;
        }
        
        // Fill the cache if it's empty
        if (created)
        {
            var messages = await _db.Messages
                .AsNoTracking()
                .Include(x => x.ReplyToMessage)
                .Include(x => x.Reactions)
                .Where(m => m.ChannelId == channelId)
                .OrderByDescending(m => m.Id)
                .Take(50)
                .Reverse() // Reverse the order to get the oldest messages first
                .Select(x => x.ToModel())
                .ToArrayAsync();
            
            foreach (var message in messages)
            {
                cache.Enqueue(message);
            }
            
            var snapshot = cache.ToListAscending();
            
            // Create snapshot
            _lastMessageCacheSnapshots[channelId] = snapshot;
            
            return snapshot;
        }
        else
        {
            // Try to get snapshot 
            if (_lastMessageCacheSnapshots.TryGetValue(channelId, out var snapshot))
            {
                return snapshot;
            }
            
            // Create snapshot
            snapshot = cache.ToListAscending();
            _lastMessageCacheSnapshots[channelId] = snapshot;
            
            return snapshot;
        }
    }

    public void AddMessage(Message message)
    {
        var channelId = message.ChannelId;
        
        // Add to the circular buffer, generate new snapshot
        if (_lastMessagesCaches.TryGetValue(channelId, out var cache))
        {
            cache.Enqueue(message);
            _lastMessageCacheSnapshots[channelId] = cache.ToListAscending();
        }
    }
    
    public void RemoveMessage(long channelId, long messageId)
    {
        if (_lastMessagesCaches.TryGetValue(channelId, out var cache))
        {
            cache.RemoveWhere(m => m.Id == messageId);
            _lastMessageCacheSnapshots[channelId] = cache.ToListAscending();
        }
    }
    
    public void ReplaceMessage(Message message)
    {
        var channelId = message.ChannelId;
        
        if (_lastMessagesCaches.TryGetValue(channelId, out var cache))
        {
            cache.ReplaceWhere(m => m.Id == message.Id, message);
            _lastMessageCacheSnapshots[channelId] = cache.ToListAscending();
        }
    }

    public Task<List<Message>> GetLastMessagesAsync(long channelId)
        => GetCachedMessagesAsync(channelId);

    public async Task<List<PlanetMember>> GetCachedChatPlanetMembersAsync(long channelId)
    {
        var created = false;
        if (!_lastChattersCaches.TryGetValue(channelId, out var cache))
        {
            cache = new ConcurrentCircularBuffer<PlanetMember>(50);
            _lastChattersCaches[channelId] = cache;
            created = true;
        }
        
        // Fill the cache if it's empty
        if (created)
        {
            // Get the last 50 messages' authors
            var members = await _db.Messages
                .AsNoTracking()
                .Include(m => m.AuthorMember)
                .ThenInclude(me => me.User)
                .Where(m => m.ChannelId == channelId && m.AuthorMemberId != null)
                .OrderByDescending(m => m.Id)
                .Take(50)
                .Select(m => m.AuthorMember.ToModel())
                .ToArrayAsync();

            HashSet<long> added = new();
            
            foreach (var member in members)
            {
                if (member is not null && !added.Contains(member.Id))
                {
                    cache.Enqueue(member);
                    added.Add(member.Id);
                }
            }
            
            var snapshot = cache.ToListAscending();
            
            // Create snapshot
            _lastChattersCacheSnapshots[channelId] = snapshot;
            
            return snapshot;
        }
        else
        {
            // Try to get snapshot
            if (_lastChattersCacheSnapshots.TryGetValue(channelId, out var snapshot))
            {
                return snapshot;
            }
            
            // Create snapshot
            snapshot = cache.ToListAscending();
            
            _lastChattersCacheSnapshots[channelId] = snapshot;
            
            return snapshot;
        }
    }
    
    public void AddChatPlanetMember(long channelId, PlanetMember member)
    {
        // Add to the circular buffer, generate new snapshot
        if (_lastChattersCaches.TryGetValue(channelId, out var cache))
        {
            // Remove any existing member with the same ID
            cache.RemoveWhere(m => m.Id == member.Id);
            cache.Enqueue(member);
            
            _lastChattersCacheSnapshots[channelId] = cache.ToListAscending();
        }
    }
    
    public void RemoveChatPlanetMember(long channelId, long memberId)
    {
        if (_lastChattersCaches.TryGetValue(channelId, out var cache))
        {
            cache.RemoveWhere(m => m.Id == memberId);
            _lastChattersCacheSnapshots[channelId] = cache.ToListAscending();
        }
    }
    
    public void ReplaceChatPlanetMember(long channelId, PlanetMember member)
    {
        if (_lastChattersCaches.TryGetValue(channelId, out var cache))
        {
            cache.ReplaceWhere(m => m.Id == member.Id, member);
            _lastChattersCacheSnapshots[channelId] = cache.ToListAscending();
        }
    }
}