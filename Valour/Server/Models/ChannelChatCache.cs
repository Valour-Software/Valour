using System.Collections.Concurrent;

namespace Valour.Server.Models;

/// <summary>
/// The channel chat cache stores chat-related cache data
/// </summary>
public class ChannelChatCache
{
    // TODO: Finish this
    
    /// <summary>
    /// Cached last messages in the channel
    /// </summary>
    private ConcurrentDictionary<long, ConcurrentCircularBuffer<Message>> _lastMessagesCaches { get; set; }
        = new ();
    
    /// <summary>
    /// Cached last chatters in the channel
    /// </summary>
    private ConcurrentDictionary<long, ConcurrentCircularBuffer<PlanetMember>> _lastChattersCaches { get; set; }
        = new ();
}