using System.Collections.Concurrent;

namespace Valour.Server.Services;

public class UserOnlineService
{
    private static readonly ConcurrentDictionary<long, DateTime?> UserTimeCache = new();
    private const int BatchSize = 256;
    private static readonly TimeSpan PlanetConnectionRefreshInterval = TimeSpan.FromMinutes(5);

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
        IReadOnlyCollection<UserOnlineUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        if (updates is null || updates.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var dueUsers = new Dictionary<long, bool>();

        foreach (var update in updates)
        {
            UserTimeCache.TryGetValue(update.UserId, out var lastActiveCached);
            var lastActive = lastActiveCached ?? DateTime.MinValue;

            if (lastActive.AddSeconds(30) >= now)
                continue;

            if (dueUsers.TryGetValue(update.UserId, out var existingMobile))
                dueUsers[update.UserId] = existingMobile || update.IsMobile;
            else
                dueUsers[update.UserId] = update.IsMobile;
        }

        if (dueUsers.Count > 0)
        {
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

            if (changedUsers.Count > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);

                foreach (var user in changedUsers)
                {
                    await _hubService.NotifyUserChange(user.ToModel());
                }
            }
        }

        await UpdatePlanetConnectionTimesAsync(updates, now, cancellationToken);
    }

    private async Task UpdatePlanetConnectionTimesAsync(
        IReadOnlyCollection<UserOnlineUpdate> updates,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var refreshCutoff = now - PlanetConnectionRefreshInterval;

        foreach (var update in updates)
        {
            if (update.PlanetIds is null || update.PlanetIds.Length == 0)
                continue;

            foreach (var batch in update.PlanetIds.Distinct().Chunk(BatchSize))
            {
                var planetIds = batch.ToArray();
                await _db.PlanetMembers
                    .Where(x => x.UserId == update.UserId &&
                                planetIds.Contains(x.PlanetId) &&
                                x.TimeLastConnected < refreshCutoff)
                    .ExecuteUpdateAsync(x => x.SetProperty(
                        p => p.TimeLastConnected,
                        now), cancellationToken);
            }
        }
    }
}
