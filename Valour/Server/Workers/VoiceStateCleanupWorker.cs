using StackExchange.Redis;
using Valour.Server.Redis;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Server.Workers;

public class VoiceStateCleanupWorker : BackgroundService
{
    private readonly ILogger<VoiceStateCleanupWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnectionMultiplexer _redis;
    private readonly IVoiceProvider _voiceProvider;
    private const int MinimumRealtimeKitParticipants = 2;

    /// <summary>
    /// Provider reconciliation runs every Nth cleanup cycle (~every 2 minutes at 30s intervals).
    /// </summary>
    private const int ReconciliationCycleInterval = 4;
    private int _cycleCount;

    private const string IndexBackfillLockKey = "voice:channels:backfill";
    private static readonly TimeSpan IndexBackfillInterval = TimeSpan.FromMinutes(5);
    private readonly string _nodeToken = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    /// <summary>
    /// Removes a channel id from the active channel index only if its channel set is gone.
    /// KEYS[1] = voice:channel:{channelId}, KEYS[2] = active channel index, ARGV[1] = channelId.
    /// </summary>
    private const string PruneIndexLuaScript = @"
if redis.call('EXISTS', KEYS[1]) == 0 then
    return redis.call('SREM', KEYS[2], ARGV[1])
end
return 0
";

    /// <summary>
    /// Removes users from a channel set when their voice:user key is missing or holds a
    /// different channel id, and returns the removed user ids.
    /// KEYS[1] = voice:channel:{channelId}, ARGV[1] = channelId, ARGV[2..] = user ids.
    /// </summary>
    private const string RemoveStaleMembersLuaScript = @"
local removed = {}
for i = 2, #ARGV do
    local current = redis.call('GET', 'voice:user:' .. ARGV[i])
    if (not current) or (tonumber(current) ~= nil and current ~= ARGV[1]) then
        redis.call('SREM', KEYS[1], ARGV[i])
        table.insert(removed, ARGV[i])
    end
end
return removed
";

    public VoiceStateCleanupWorker(
        ILogger<VoiceStateCleanupWorker> logger,
        IServiceProvider serviceProvider,
        IConnectionMultiplexer redis,
        IVoiceProvider voiceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _redis = redis;
        _voiceProvider = voiceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunStartupCleanupAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                await CleanupStaleVoiceStateAsync();

                _cycleCount++;
                if (_cycleCount >= ReconciliationCycleInterval)
                {
                    _cycleCount = 0;
                    await ReconcileWithProviderAsync();
                    await CleanupTrackedMeetingsAsync("periodic sweep");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in voice state cleanup worker");
            }
        }
    }

    private async Task RunStartupCleanupAsync()
    {
        try
        {
            await CleanupStaleVoiceStateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during voice state startup cleanup");
        }

        try
        {
            await CleanupTrackedMeetingsAsync("startup sweep");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during voice meeting startup cleanup");
        }
    }

    /// <summary>
    /// Removes channel set members whose voice:user key expired or now points elsewhere.
    /// Channels are enumerated from the <see cref="VoiceStateService.ActiveChannelsKey"/>
    /// index rather than a keyspace scan. Each node only removes stale users from channels
    /// of planets it hosts (or channels whose planet no longer exists), so the node that
    /// owns the in-memory participant list is the one that updates it and broadcasts.
    /// </summary>
    private async Task CleanupStaleVoiceStateAsync()
    {
        var db = _redis.GetDatabase(RedisDbTypes.Cluster);

        await BackfillActiveChannelIndexAsync(db);

        var indexedChannels = await db.SetMembersAsync(VoiceStateService.ActiveChannelsKey);
        if (indexedChannels.Length == 0)
            return;

        using var scope = _serviceProvider.CreateScope();
        var hostedPlanetService = scope.ServiceProvider.GetRequiredService<HostedPlanetService>();
        var coreHub = scope.ServiceProvider.GetRequiredService<CoreHubService>();
        var valourDb = scope.ServiceProvider.GetRequiredService<ValourDb>();

        foreach (var indexedChannel in indexedChannels)
        {
            if (!long.TryParse((string?)indexedChannel, out var channelId))
            {
                await db.SetRemoveAsync(VoiceStateService.ActiveChannelsKey, indexedChannel);
                continue;
            }

            var channelKey = (RedisKey)$"voice:channel:{channelId}";
            var members = await db.SetMembersAsync(channelKey);
            if (members.Length == 0)
            {
                // Redis deletes empty sets; drop the index entry unless a join recreated it.
                await db.ScriptEvaluateAsync(PruneIndexLuaScript,
                    new[] { channelKey, (RedisKey)VoiceStateService.ActiveChannelsKey },
                    new RedisValue[] { channelId });
                continue;
            }

            var userIds = new List<long>(members.Length);
            foreach (var member in members)
            {
                if (!long.TryParse((string?)member, out var userId))
                {
                    await db.SetRemoveAsync(channelKey, member);
                    continue;
                }

                userIds.Add(userId);
            }

            if (userIds.Count == 0)
                continue;

            // One MGET for every participant's current channel
            var userKeys = new RedisKey[userIds.Count];
            for (int i = 0; i < userIds.Count; i++)
                userKeys[i] = $"voice:user:{userIds[i]}";

            var userChannels = await db.StringGetAsync(userKeys);

            var candidateUserIds = new List<long>();
            for (int i = 0; i < userIds.Count; i++)
            {
                var userChannel = userChannels[i];

                // User key expired or points to a different channel
                if (!userChannel.HasValue ||
                    (long.TryParse((string?)userChannel, out var currentChannelId) && currentChannelId != channelId))
                {
                    candidateUserIds.Add(userIds[i]);
                }
            }

            if (candidateUserIds.Count == 0)
                continue;

            // Look up the channel to find its planet
            var dbChannel = await valourDb.Channels
                .AsNoTracking()
                .Where(x => x.Id == channelId)
                .Select(x => new { x.Id, x.PlanetId })
                .FirstOrDefaultAsync();

            if (dbChannel?.PlanetId is null)
            {
                await RemoveStaleChannelMembersAsync(db, channelKey, channelId, candidateUserIds);
                continue;
            }

            var hostedResult = await hostedPlanetService.TryGetAsync(dbChannel.PlanetId.Value);
            var hosted = hostedResult.HostedPlanet;
            if (hosted is null)
            {
                // A planet that no longer exists has no host to update. A planet hosted on
                // another node is left to that node, which owns its participant list.
                if (hostedResult.CorrectNode == HostedPlanetResult.DoesNotExist.CorrectNode)
                    await RemoveStaleChannelMembersAsync(db, channelKey, channelId, candidateUserIds);

                continue;
            }

            var staleUserIds = await RemoveStaleChannelMembersAsync(db, channelKey, channelId, candidateUserIds);
            if (staleUserIds.Count == 0)
                continue;

            foreach (var userId in staleUserIds)
            {
                hosted.RemoveVoiceParticipant(channelId, userId);
            }

            // Get remaining participants and broadcast
            var remainingMembers = await db.SetMembersAsync(channelKey);
            var remainingUserIds = remainingMembers
                .Select(m => long.TryParse((string?)m, out var id) ? id : 0)
                .Where(id => id > 0)
                .ToList();

            // Clean up empty sets
            if (remainingUserIds.Count == 0)
            {
                await db.KeyDeleteAsync(channelKey);
                hosted.SetVoiceParticipants(channelId, new List<long>());
            }

            if (remainingUserIds.Count < MinimumRealtimeKitParticipants)
            {
                await _voiceProvider.CloseTrackedMeetingAsync(
                    channelId,
                    "voice state cleanup left fewer than two participants");
            }

            coreHub.NotifyVoiceChannelParticipants(dbChannel.PlanetId.Value, new VoiceChannelParticipantsUpdate
            {
                PlanetId = dbChannel.PlanetId.Value,
                ChannelId = channelId,
                UserIds = remainingUserIds
            });

            _logger.LogInformation(
                "Cleaned {Count} stale voice participants from channel {ChannelId}",
                staleUserIds.Count, channelId);
        }
    }

    /// <summary>
    /// Atomically removes the given users from a channel set, skipping any user whose
    /// voice:user key points at the channel again (they rejoined after the stale check).
    /// Returns the users that were removed.
    /// </summary>
    private static async Task<List<long>> RemoveStaleChannelMembersAsync(
        IDatabase db, RedisKey channelKey, long channelId, List<long> userIds)
    {
        var args = new RedisValue[userIds.Count + 1];
        args[0] = channelId;
        for (int i = 0; i < userIds.Count; i++)
            args[i + 1] = userIds[i];

        var result = await db.ScriptEvaluateAsync(RemoveStaleMembersLuaScript, new[] { channelKey }, args);

        var removed = new List<long>();
        if (result.IsNull)
            return removed;

        foreach (var value in (RedisValue[])result!)
        {
            if (long.TryParse((string?)value, out var userId))
                removed.Add(userId);
        }

        return removed;
    }

    /// <summary>
    /// Adds channels found by a keyspace scan to the active channel index. This covers
    /// channel sets written before the index existed or by nodes running older code. A Redis
    /// lock that is left to expire limits the scan to one node per interval.
    /// </summary>
    private async Task BackfillActiveChannelIndexAsync(IDatabase db)
    {
        try
        {
            if (!await db.LockTakeAsync(IndexBackfillLockKey, _nodeToken, IndexBackfillInterval))
                return;

            var channelIds = new HashSet<RedisValue>();
            foreach (var server in _redis.GetServers())
            {
                if (server.IsReplica)
                    continue;

                await foreach (var key in server.KeysAsync(RedisDbTypes.Cluster, "voice:channel:*"))
                {
                    var channelIdStr = key.ToString().Replace("voice:channel:", "");
                    if (long.TryParse(channelIdStr, out var channelId))
                        channelIds.Add(channelId);
                }
            }

            if (channelIds.Count > 0)
                await db.SetAddAsync(VoiceStateService.ActiveChannelsKey, channelIds.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not backfill the active voice channel index");
        }
    }

    /// <summary>
    /// Reconciles the backend's actual connected participants against Redis state,
    /// removing users from Redis/HostedPlanet that the backend no longer sees. The
    /// backend's live truth is fetched provider-agnostically via
    /// <see cref="IVoiceProvider.GetConnectedUserIdsAsync"/>; a null result (backend
    /// unreachable, or no positive evidence of who is connected) skips that channel
    /// so live participants are never wrongly removed.
    /// </summary>
    private async Task ReconcileWithProviderAsync()
    {
        var trackedMeetings = _voiceProvider.GetTrackedChannelMeetingIds();
        if (trackedMeetings.Count == 0)
        {
            await _voiceProvider.CloseOrphanedSessionsAsync(MinimumRealtimeKitParticipants);
            return;
        }

        var db = _redis.GetDatabase(RedisDbTypes.Cluster);

        using var scope = _serviceProvider.CreateScope();
        var hostedPlanetService = scope.ServiceProvider.GetRequiredService<HostedPlanetService>();
        var coreHub = scope.ServiceProvider.GetRequiredService<CoreHubService>();
        var valourDb = scope.ServiceProvider.GetRequiredService<ValourDb>();

        foreach (var (channelId, meetingId) in trackedMeetings)
        {
            try
            {
                // Direct/group calls use the same provider room tracking, but their
                // lifecycle is stored in direct_calls rather than Redis planet voice
                // presence. DirectCallCleanupWorker owns those rooms.
                if (await valourDb.DirectCalls.AsNoTracking().AnyAsync(x =>
                        x.Id == channelId && x.State != DirectCallState.Ended))
                    continue;

                // Get the set of user IDs Redis thinks are in this channel
                var redisMembers = await db.SetMembersAsync($"voice:channel:{channelId}");
                var redisUserIds = new HashSet<long>();
                foreach (var member in redisMembers)
                {
                    if (long.TryParse((string?)member, out var uid))
                        redisUserIds.Add(uid);
                }

                if (redisUserIds.Count < MinimumRealtimeKitParticipants)
                {
                    await _voiceProvider.CloseTrackedMeetingAsync(
                        channelId,
                        "reconciliation found fewer than two Valour participants");
                    continue;
                }

                // Ask the backend who is actually connected. Null = could not query;
                // skip so we don't remove live participants on a transient failure.
                var connectedUserIds = await _voiceProvider.GetConnectedUserIdsAsync(channelId, meetingId);
                if (connectedUserIds is null)
                    continue;

                // Users connected on the backend but unknown to Valour were never
                // admitted or have since been removed (left, kicked, or lost
                // access). A token that is still valid must not keep them in the
                // call, so eject them from the media backend.
                foreach (var intruderId in connectedUserIds.Except(redisUserIds))
                {
                    // Recheck: the user may have joined after the snapshot above.
                    if (await db.SetContainsAsync($"voice:channel:{channelId}", intruderId))
                        continue;

                    await _voiceProvider.KickUserFromTrackedChannelAsync(channelId, intruderId);
                    _logger.LogInformation(
                        "Voice reconciliation removed untracked user {UserId} from channel {ChannelId}",
                        intruderId, channelId);
                }

                // Find users in Redis but NOT connected on the backend
                var staleUserIds = redisUserIds.Except(connectedUserIds).ToList();
                if (staleUserIds.Count == 0)
                    continue;

                // Remove stale users from Redis
                foreach (var userId in staleUserIds)
                {
                    await db.SetRemoveAsync($"voice:channel:{channelId}", userId);

                    // Only clear user key if it still points to this channel
                    var userKey = $"voice:user:{userId}";
                    var currentChannel = await db.StringGetAsync(userKey);
                    if (currentChannel.HasValue &&
                        long.TryParse((string?)currentChannel, out var currentChannelId) &&
                        currentChannelId == channelId)
                    {
                        await db.KeyDeleteAsync(userKey);
                    }
                }

                // Look up the channel's planet for HostedPlanet + broadcast
                var dbChannel = await valourDb.Channels
                    .AsNoTracking()
                    .Where(x => x.Id == channelId)
                    .Select(x => new { x.Id, x.PlanetId })
                    .FirstOrDefaultAsync();

                if (dbChannel?.PlanetId is null)
                    continue;

                var hostedResult = await hostedPlanetService.TryGetAsync(dbChannel.PlanetId.Value);
                var hosted = hostedResult.HostedPlanet;
                if (hosted is null)
                    continue;

                foreach (var userId in staleUserIds)
                {
                    hosted.RemoveVoiceParticipant(channelId, userId);
                }

                // Get remaining participants and broadcast
                var remainingMembers = await db.SetMembersAsync($"voice:channel:{channelId}");
                var remainingUserIds = remainingMembers
                    .Select(m => long.TryParse((string?)m, out var id) ? id : 0)
                    .Where(id => id > 0)
                    .ToList();

                if (remainingUserIds.Count == 0)
                {
                    await db.KeyDeleteAsync($"voice:channel:{channelId}");
                    hosted.SetVoiceParticipants(channelId, new List<long>());
                    _voiceProvider.RemoveMeetingMapping(channelId);
                }

                if (remainingUserIds.Count < MinimumRealtimeKitParticipants)
                {
                    await _voiceProvider.CloseTrackedMeetingAsync(
                        channelId,
                        "reconciliation left fewer than two participants");
                }

                coreHub.NotifyVoiceChannelParticipants(dbChannel.PlanetId.Value,
                    new VoiceChannelParticipantsUpdate
                    {
                        PlanetId = dbChannel.PlanetId.Value,
                        ChannelId = channelId,
                        UserIds = remainingUserIds
                    });

                _logger.LogInformation(
                    "Voice reconciliation removed {Count} stale participants from channel {ChannelId}",
                    staleUserIds.Count, channelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reconciling channel {ChannelId} with the voice backend", channelId);
            }
        }

        await _voiceProvider.CloseOrphanedSessionsAsync(MinimumRealtimeKitParticipants);
    }

    private async Task CleanupTrackedMeetingsAsync(string reason)
    {
        var trackedMeetings = await _voiceProvider.LoadOpenMeetingMappingsAsync();
        var participantCountsByChannel = await GetRedisParticipantCountsByChannelAsync(trackedMeetings.Select(x => x.Key));
        if (participantCountsByChannel is null)
        {
            _logger.LogWarning(
                "Skipping voice meeting cleanup ({Reason}): Redis participant counts are unavailable",
                reason);
            return;
        }

        var checkedMeetings = 0;
        var keptMeetings = 0;
        var closedMeetings = 0;
        var failedMeetings = 0;

        using var scope = _serviceProvider.CreateScope();
        var valourDb = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var activeDirectCallIds = await valourDb.DirectCalls.AsNoTracking()
            .Where(x => x.State != DirectCallState.Ended)
            .Select(x => x.Id)
            .ToHashSetAsync();

        foreach (var (channelId, meetingId) in trackedMeetings)
        {
            if (string.IsNullOrWhiteSpace(meetingId))
                continue;
            if (activeDirectCallIds.Contains(channelId))
                continue;

            checkedMeetings++;
            if (participantCountsByChannel.TryGetValue(channelId, out var participantCount) &&
                participantCount >= MinimumRealtimeKitParticipants)
            {
                keptMeetings++;
                continue;
            }

            var closeResult = await _voiceProvider.CloseMeetingAsync(
                meetingId,
                $"{reason} found tracked Valour voice channel with fewer than two participants",
                channelId);

            if (closeResult.Success)
            {
                _voiceProvider.RemoveMeetingMapping(channelId);
                closedMeetings++;
            }
            else
            {
                failedMeetings++;
            }
        }

        _logger.LogInformation(
            "Voice {Reason} checked {Checked} tracked meetings; kept {Kept}, closed {Closed}, failed {Failed}",
            reason,
            checkedMeetings,
            keptMeetings,
            closedMeetings,
            failedMeetings);
    }

    /// <summary>
    /// Returns the number of live participants for each requested channel: set members whose
    /// voice:user key still points at the channel. Returns null when Redis cannot be read;
    /// the caller must skip cleanup rather than treat every meeting as empty and close live calls.
    /// </summary>
    private async Task<Dictionary<long, int>?> GetRedisParticipantCountsByChannelAsync(IEnumerable<long> channelIds)
    {
        var result = new Dictionary<long, int>();

        try
        {
            var db = _redis.GetDatabase(RedisDbTypes.Cluster);

            foreach (var channelId in channelIds)
            {
                if (result.ContainsKey(channelId))
                    continue;

                var members = await db.SetMembersAsync($"voice:channel:{channelId}");
                var userKeys = new List<RedisKey>(members.Length);
                foreach (var member in members)
                {
                    if (long.TryParse((string?)member, out var userId))
                        userKeys.Add($"voice:user:{userId}");
                }

                var count = 0;
                if (userKeys.Count > 0)
                {
                    // One MGET for every participant's current channel
                    var userChannels = await db.StringGetAsync(userKeys.ToArray());
                    foreach (var userChannel in userChannels)
                    {
                        if (userChannel.HasValue &&
                            long.TryParse((string?)userChannel, out var currentChannelId) &&
                            currentChannelId == channelId)
                        {
                            count++;
                        }
                    }
                }

                result[channelId] = count;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not read Redis voice participant counts for meeting cleanup; skipping this cleanup pass");
            return null;
        }

        return result;
    }
}
