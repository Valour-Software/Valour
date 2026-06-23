using StackExchange.Redis;
using Valour.Server.Redis;

namespace Valour.Server.Services;

public class ChannelWatchingService
{
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan KeyTtl = TimeSpan.FromHours(12);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ChannelWatchingService> _logger;

    public ChannelWatchingService(
        IConnectionMultiplexer redis,
        ILogger<ChannelWatchingService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task RefreshAsync(long userId, long channelId, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            return;

        var db = _redis.GetDatabase(RedisDbTypes.Cluster);
        var channelKey = GetChannelKey(channelId);
        var connectionKey = GetConnectionKey(connectionId);
        var expiresAt = DateTimeOffset.UtcNow.Add(LeaseTtl).ToUnixTimeMilliseconds();
        var member = GetMember(userId, connectionId);

        await Task.WhenAll(
            db.SortedSetAddAsync(channelKey, member, expiresAt),
            db.KeyExpireAsync(channelKey, KeyTtl),
            db.SetAddAsync(connectionKey, channelId),
            db.KeyExpireAsync(connectionKey, LeaseTtl.Add(TimeSpan.FromSeconds(10)))
        );
    }

    public async Task ClearAsync(long userId, long channelId, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            return;

        var db = _redis.GetDatabase(RedisDbTypes.Cluster);

        await Task.WhenAll(
            db.SortedSetRemoveAsync(GetChannelKey(channelId), GetMember(userId, connectionId)),
            db.SetRemoveAsync(GetConnectionKey(connectionId), channelId)
        );
    }

    public async Task ClearConnectionAsync(long userId, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            return;

        var db = _redis.GetDatabase(RedisDbTypes.Cluster);
        var connectionKey = GetConnectionKey(connectionId);
        var channelIds = await db.SetMembersAsync(connectionKey);

        if (channelIds.Length == 0)
            return;

        var member = GetMember(userId, connectionId);
        var tasks = new List<Task>(channelIds.Length + 1);

        foreach (var value in channelIds)
        {
            if (!long.TryParse(value.ToString(), out var channelId))
            {
                _logger.LogWarning(
                    "Invalid active channel view entry {Entry} for connection {ConnectionId}",
                    value.ToString(),
                    connectionId);
                continue;
            }

            tasks.Add(db.SortedSetRemoveAsync(GetChannelKey(channelId), member));
        }

        tasks.Add(db.KeyDeleteAsync(connectionKey));
        await Task.WhenAll(tasks);
    }

    public async Task<HashSet<long>> GetActiveViewingUserIdsAsync(long channelId)
    {
        var db = _redis.GetDatabase(RedisDbTypes.Cluster);
        var channelKey = GetChannelKey(channelId);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await db.SortedSetRemoveRangeByScoreAsync(channelKey, double.NegativeInfinity, now);

        var activeMembers = await db.SortedSetRangeByScoreAsync(
            channelKey,
            now + 1,
            double.PositiveInfinity);

        var userIds = new HashSet<long>();
        foreach (var member in activeMembers)
        {
            if (TryGetUserId(member.ToString(), out var userId))
                userIds.Add(userId);
        }

        return userIds;
    }

    public async Task<bool> IsUserViewingChannelAsync(long userId, long channelId)
    {
        var activeUserIds = await GetActiveViewingUserIdsAsync(channelId);
        return activeUserIds.Contains(userId);
    }

    public async Task<long[]> FilterUsersNotViewingChannelAsync(long channelId, IEnumerable<long> userIds)
    {
        var recipients = userIds?.Distinct().ToArray() ?? [];
        if (recipients.Length == 0)
            return [];

        var activeUserIds = await GetActiveViewingUserIdsAsync(channelId);
        if (activeUserIds.Count == 0)
            return recipients;

        return recipients
            .Where(userId => !activeUserIds.Contains(userId))
            .ToArray();
    }

    private static RedisKey GetChannelKey(long channelId) => $"channel:viewing:{channelId}";

    private static RedisKey GetConnectionKey(string connectionId) => $"channel:viewing:connection:{connectionId}";

    private static RedisValue GetMember(long userId, string connectionId) => $"{userId}:{connectionId}";

    private static bool TryGetUserId(string member, out long userId)
    {
        userId = 0;
        var separatorIndex = member.IndexOf(':');
        return separatorIndex > 0 && long.TryParse(member.AsSpan(0, separatorIndex), out userId);
    }
}
