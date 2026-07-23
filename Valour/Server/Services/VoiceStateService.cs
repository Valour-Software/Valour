using System.Collections.Concurrent;
using StackExchange.Redis;
using Valour.Server.Redis;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class VoiceStateService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly HostedPlanetService _hostedPlanetService;
    private readonly CoreHubService _coreHub;
    private readonly ILogger<VoiceStateService> _logger;

    private static readonly TimeSpan UserKeyTtl = TimeSpan.FromSeconds(120);
    private const string UserSessionKeyPrefix = "voice:user:session:";

    /// <summary>
    /// Per-user lock to serialize join/leave operations and prevent races.
    /// </summary>
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> UserLocks = new();

    /// <summary>
    /// Lua script that atomically swaps the user's voice channel.
    /// KEYS[1] = voice:user:{userId}
    /// ARGV[1] = new channel ID
    /// ARGV[2] = TTL in seconds
    /// ARGV[3] = userId (for SET operations)
    /// Returns the old channel ID (or nil).
    /// </summary>
    private const string JoinLuaScript = @"
local oldChannelId = redis.call('GET', KEYS[1])
redis.call('SET', KEYS[1], ARGV[1], 'EX', ARGV[2])
if oldChannelId and oldChannelId ~= ARGV[1] then
    redis.call('SREM', 'voice:channel:' .. oldChannelId, ARGV[3])
end
redis.call('SADD', 'voice:channel:' .. ARGV[1], ARGV[3])
return oldChannelId
";

    public VoiceStateService(
        IConnectionMultiplexer redis,
        HostedPlanetService hostedPlanetService,
        CoreHubService coreHub,
        ILogger<VoiceStateService> logger)
    {
        _redis = redis;
        _hostedPlanetService = hostedPlanetService;
        _coreHub = coreHub;
        _logger = logger;
    }

    /// <summary>
    /// Called when a user joins a voice channel. Returns the previous channel ID if the user was in a different one.
    /// Uses a Lua script for atomic get+set+cleanup and a per-user lock to serialize operations.
    /// </summary>
    public async Task<long?> UserJoinVoiceChannelAsync(long userId, long channelId, long planetId, string? sessionId = null)
    {
        var userLock = UserLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync();
        try
        {
            var db = _redis.GetDatabase(RedisDbTypes.Cluster);
            var userKey = $"voice:user:{userId}";
            var userSessionKey = $"{UserSessionKeyPrefix}{userId}";

            // Atomic: get old channel, set new channel+TTL, move between channel sets
            var result = await db.ScriptEvaluateAsync(
                JoinLuaScript,
                new RedisKey[] { userKey },
                new RedisValue[] { channelId, (int)UserKeyTtl.TotalSeconds, userId });

            long? previousChannelId = null;
            var oldValue = (string?)result;
            if (oldValue is not null && long.TryParse(oldValue, out var oldChannelId) && oldChannelId != channelId)
            {
                previousChannelId = oldChannelId;

                // Update HostedPlanet for old channel
                var oldHosted = await _hostedPlanetService.GetRequiredAsync(planetId);
                oldHosted.RemoveVoiceParticipant(oldChannelId, userId);
                BroadcastChannelParticipants(oldHosted, oldChannelId, planetId);
            }

            // Update HostedPlanet for new channel
            var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
            hosted.AddVoiceParticipant(channelId, userId);
            BroadcastChannelParticipants(hosted, channelId, planetId);

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                await db.KeyDeleteAsync(userSessionKey);
            }
            else
            {
                await db.StringSetAsync(userSessionKey, sessionId, UserKeyTtl);
            }

            return previousChannelId;
        }
        finally
        {
            userLock.Release();
        }
    }

    /// <summary>
    /// Called when a user leaves a voice channel.
    /// </summary>
    public async Task UserLeaveVoiceChannelAsync(long userId, long channelId, long planetId, string? sessionId = null)
    {
        var db = _redis.GetDatabase(RedisDbTypes.Cluster);
        var userKey = $"voice:user:{userId}";
        var userSessionKey = $"{UserSessionKeyPrefix}{userId}";

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var currentSession = await db.StringGetAsync(userSessionKey);
            if (currentSession.HasValue &&
                !string.Equals((string?)currentSession, sessionId, StringComparison.Ordinal))
            {
                _logger.LogDebug(
                    "Ignoring stale voice leave for user {UserId} in channel {ChannelId}. Session {SessionId} != {CurrentSessionId}",
                    userId,
                    channelId,
                    sessionId,
                    (string?)currentSession);
                return;
            }
        }

        // Only delete user key if it still points to this channel
        var existing = await db.StringGetAsync(userKey);
        if (existing.HasValue && long.TryParse((string?)existing, out var currentChannelId) && currentChannelId == channelId)
        {
            await db.KeyDeleteAsync(userKey);
            await db.KeyDeleteAsync(userSessionKey);
        }

        // Remove from channel set
        await db.SetRemoveAsync($"voice:channel:{channelId}", userId);

        // Update HostedPlanet
        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
        if (hosted is not null)
        {
            hosted.RemoveVoiceParticipant(channelId, userId);
            BroadcastChannelParticipants(hosted, channelId, planetId);
        }
    }

    /// <summary>
    /// Refreshes the TTL on a user's voice key (heartbeat).
    /// </summary>
    public async Task RefreshVoiceHeartbeatAsync(long userId)
    {
        var db = _redis.GetDatabase(RedisDbTypes.Cluster);
        var userKey = $"voice:user:{userId}";
        await db.KeyExpireAsync(userKey, UserKeyTtl);
        await db.KeyExpireAsync($"{UserSessionKeyPrefix}{userId}", UserKeyTtl);
    }

    /// <summary>
    /// Gets the list of user IDs in a voice channel from Redis.
    /// </summary>
    public async Task<List<long>> GetChannelParticipantsAsync(long channelId)
    {
        var db = _redis.GetDatabase(RedisDbTypes.Cluster);
        var members = await db.SetMembersAsync($"voice:channel:{channelId}");
        var result = new List<long>(members.Length);
        foreach (var member in members)
        {
            if (long.TryParse((string?)member, out var userId))
                result.Add(userId);
        }
        return result;
    }

    private void BroadcastChannelParticipants(HostedPlanet hosted, long channelId, long planetId)
    {
        var channel = hosted.GetChannel(channelId);
        if (channel is null)
            return;

        var participants = hosted.GetAllVoiceParticipants();
        var userIds = participants.TryGetValue(channelId, out var list) ? list : new List<long>();

        _coreHub.NotifyVoiceChannelParticipants(planetId, new VoiceChannelParticipantsUpdate
        {
            PlanetId = planetId,
            ChannelId = channelId,
            UserIds = userIds
        });
    }
}
