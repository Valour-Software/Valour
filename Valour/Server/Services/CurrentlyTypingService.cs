using System.Collections.Concurrent;

namespace Valour.Server.Services;

public class CurrentlyTypingService
{
    private readonly CoreHubService _hubService;
    
    // Map of ChannelId to the UserId who was typing and the Time at which they were typing
    private static readonly ConcurrentDictionary<long, ConcurrentDictionary<long, DateTime?>> CurrentlyTyping = new();

    // Seconds before allowed to send another event
    private const int REFRESH = 3;
    
    // Seconds before removed from event list for cleanup
    private const int CLEAR = 5;
    
    public CurrentlyTypingService(CoreHubService hubService)
    {
        _hubService = hubService;
    }

    public void AddCurrentlyTyping(long channelId, long userId)
    {
        CurrentlyTyping.TryGetValue(channelId, out var usersTyping);
            
        // If there are is not already a users typing collection for the channel,
        // create one and add our new typer. Then send the result (it's new)
        if (usersTyping is null)
        {
            usersTyping = new ConcurrentDictionary<long, DateTime?>
            {
                [userId] = DateTime.UtcNow
            };

            CurrentlyTyping[channelId] = usersTyping;
                
            _ = _hubService.NotifyCurrentlyTyping(channelId, userId);

            return;
        }
            
        // If we already have a collection, we check if it already contains the user
        usersTyping.TryGetValue(userId, out var lastTime);
            
        // If it does not contain the user...
        if (lastTime is null)
        {
            // Add the user
            usersTyping[userId] = DateTime.UtcNow;
        }
        else
        {
            // Otherwise, we check if it's been enough time to constitute another event
            if (lastTime.Value.AddSeconds(REFRESH) > DateTime.UtcNow)
            {
                // If not, we do nothing. 
                return;
            }
            
            // Alright, we need an event. We go ahead and update the user's time.
            usersTyping[userId] = DateTime.UtcNow;
            
            // Now, since we are sending a new event, we go through *everyone else* in the event list and see if
            // they need to be removed. This is the cleanup.
            List<long> remove = new();
            foreach (var user in usersTyping)
            {
                if (user.Value.Value.AddSeconds(CLEAR) < DateTime.UtcNow)
                {
                    remove.Add(user.Key);
                }
            }

            // Actually do the cleaning
            foreach (var removeId in remove)
            {
                usersTyping.Remove(removeId, out _);
            }
        }
    
        // Now we send the update!
        _ = _hubService.NotifyCurrentlyTyping(channelId, userId);
    }
}