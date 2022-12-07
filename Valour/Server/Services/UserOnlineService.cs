using System.Collections.Concurrent;
using Valour.Server.Database;

namespace Valour.Server.Services;

public class UserOnlineService
{
    private static readonly ConcurrentDictionary<long, DateTime?> UserTimeCache = new();

    private readonly ValourDB _db;
    private readonly CoreHubService _hubService;

    public UserOnlineService(CoreHubService hubService, ValourDB db)
    {
        _hubService = hubService;
        _db = db;
    }

    public async Task UpdateOnlineState(long userId)
    {
        UserTimeCache.TryGetValue(userId, out var lastActiveCached);

        DateTime lastActive = lastActiveCached ?? DateTime.MinValue;

        // Only bother updating if it's been at least 30 seconds
        // since the last activity update
        if (lastActive.AddSeconds(30) < DateTime.UtcNow)
        {
            var user = await _db.Users.FindAsync(userId);
            user.TimeLastActive = DateTime.UtcNow;
            UserTimeCache[user.Id] = user.TimeLastActive;
            
            // Notify of user activity change
            await _hubService.NotifyUserChange(user);
        }
    }
}