using System.Collections.Concurrent;

namespace Valour.Server.Services;

public class UserOnlineService
{
    private static readonly ConcurrentDictionary<long, DateTime?> UserTimeCache = new();
    private const int BatchSize = 256;

    private readonly ValourDb _db;
    private readonly CoreHubService _hubService;

    public UserOnlineService(CoreHubService hubService, ValourDb db)
    {
        _hubService = hubService;
        _db = db;
    }

    public async Task UpdateOnlineState(long userId, bool isMobile = false)
    {
        UserTimeCache.TryGetValue(userId, out var lastActiveCached);

        DateTime lastActive = lastActiveCached ?? DateTime.MinValue;

        // Only bother updating if it's been at least 30 seconds
        // since the last activity update
        if (lastActive.AddSeconds(30) < DateTime.UtcNow)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user is null)
                return;

            user.TimeLastActive = DateTime.UtcNow;
            user.IsMobile = isMobile;
            UserTimeCache[user.Id] = user.TimeLastActive;
            
            // Notify of user activity change
            await _hubService.NotifyUserChange(user.ToModel());
            await _db.SaveChangesAsync();
        }
    }

    public async Task UpdateOnlineStatesBatchAsync(
        IReadOnlyCollection<(long UserId, bool IsMobile)> updates,
        CancellationToken cancellationToken = default)
    {
        if (updates is null || updates.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var dueUsers = new Dictionary<long, bool>();

        foreach (var (userId, isMobile) in updates)
        {
            UserTimeCache.TryGetValue(userId, out var lastActiveCached);
            var lastActive = lastActiveCached ?? DateTime.MinValue;

            if (lastActive.AddSeconds(30) >= now)
                continue;

            if (dueUsers.TryGetValue(userId, out var existingMobile))
                dueUsers[userId] = existingMobile || isMobile;
            else
                dueUsers[userId] = isMobile;
        }

        if (dueUsers.Count == 0)
            return;

        var changedUsers = new List<Valour.Database.User>();
        var dueIds = dueUsers.Keys.ToArray();

        foreach (var batch in dueIds.Chunk(BatchSize))
        {
            var ids = batch.ToArray();
            var users = await _db.Users
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken);

            foreach (var user in users)
            {
                user.TimeLastActive = now;
                user.IsMobile = dueUsers[user.Id];
                UserTimeCache[user.Id] = now;
            }

            changedUsers.AddRange(users);
        }

        if (changedUsers.Count == 0)
            return;

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var user in changedUsers)
        {
            await _hubService.NotifyUserChange(user.ToModel());
        }
    }
}
