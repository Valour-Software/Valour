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
    private readonly RealtimeKitService _realtimeKitService;
    private const int MinimumRealtimeKitParticipants = 2;
    private static readonly TimeSpan OrphanSessionGracePeriod = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Cloudflare reconciliation runs every Nth cleanup cycle (~every 2 minutes at 30s intervals).
    /// </summary>
    private const int ReconciliationCycleInterval = 4;
    private int _cycleCount;

    public VoiceStateCleanupWorker(
        ILogger<VoiceStateCleanupWorker> logger,
        IServiceProvider serviceProvider,
        IConnectionMultiplexer redis,
        RealtimeKitService realtimeKitService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _redis = redis;
        _realtimeKitService = realtimeKitService;
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
                    await ReconcileWithCloudflareAsync();
                    await CleanupTrackedRealtimeKitMeetingsAsync("periodic sweep");
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
            await CleanupTrackedRealtimeKitMeetingsAsync("startup sweep");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during RealtimeKit startup cleanup");
        }
    }

    private async Task CleanupStaleVoiceStateAsync()
    {
        var db = _redis.GetDatabase(RedisDbTypes.Cluster);
        var server = _redis.GetServers().FirstOrDefault();
        if (server is null)
            return;

        var channelKeys = server.Keys(RedisDbTypes.Cluster, "voice:channel:*").ToList();

        using var scope = _serviceProvider.CreateScope();
        var hostedPlanetService = scope.ServiceProvider.GetRequiredService<HostedPlanetService>();
        var coreHub = scope.ServiceProvider.GetRequiredService<CoreHubService>();
        var valourDb = scope.ServiceProvider.GetRequiredService<ValourDb>();

        foreach (var channelKey in channelKeys)
        {
            var channelIdStr = channelKey.ToString().Replace("voice:channel:", "");
            if (!long.TryParse(channelIdStr, out var channelId))
                continue;

            var members = await db.SetMembersAsync(channelKey);
            var staleUserIds = new List<long>();

            foreach (var member in members)
            {
                if (!long.TryParse((string?)member, out var userId))
                {
                    await db.SetRemoveAsync(channelKey, member);
                    continue;
                }

                var userKey = $"voice:user:{userId}";
                var userChannel = await db.StringGetAsync(userKey);

                // User key expired or points to a different channel
                if (!userChannel.HasValue ||
                    (long.TryParse((string?)userChannel, out var currentChannelId) && currentChannelId != channelId))
                {
                    staleUserIds.Add(userId);
                    await db.SetRemoveAsync(channelKey, member);
                }
            }

            if (staleUserIds.Count == 0)
                continue;

            // Look up the channel to find its planet
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
                await _realtimeKitService.CloseTrackedMeetingAsync(
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
    /// Polls Cloudflare RealtimeKit to reconcile actual meeting participants against Redis state.
    /// Removes users from Redis/HostedPlanet that are no longer connected in Cloudflare.
    /// </summary>
    private async Task ReconcileWithCloudflareAsync()
    {
        var trackedMeetings = _realtimeKitService.GetTrackedChannelMeetingIds();
        if (trackedMeetings.Count == 0)
        {
            await CloseUnderpopulatedRealtimeKitSessionsAsync();
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
                    await _realtimeKitService.CloseTrackedMeetingAsync(
                        channelId,
                        "Cloudflare reconciliation found fewer than two Valour participants");
                    continue;
                }

                // Query Cloudflare for the live session(s) of this meeting
                var sessionsResult = await _realtimeKitService.GetLiveSessionsForMeetingAsync(meetingId);
                var cloudflareUserIds = new HashSet<long>();

                if (sessionsResult.Success && sessionsResult.Data is not null)
                {
                    foreach (var session in sessionsResult.Data)
                    {
                        var participantsResult =
                            await _realtimeKitService.GetSessionParticipantsAsync(session.Id);

                        if (!participantsResult.Success || participantsResult.Data is null)
                            continue;

                        foreach (var participant in participantsResult.Data)
                        {
                            // Only count participants that haven't left
                            if (!string.IsNullOrEmpty(participant.LeftAt))
                                continue;

                            var userId = participant.ExtractUserId();
                            if (userId.HasValue)
                                cloudflareUserIds.Add(userId.Value);
                        }
                    }
                }
                else
                {
                    // If the API call failed, skip reconciliation for this channel
                    // to avoid incorrectly removing participants
                    continue;
                }

                // Find users in Redis but NOT in Cloudflare
                var staleUserIds = redisUserIds.Except(cloudflareUserIds).ToList();
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
                }

                if (remainingUserIds.Count < MinimumRealtimeKitParticipants)
                {
                    await _realtimeKitService.CloseTrackedMeetingAsync(
                        channelId,
                        "Cloudflare reconciliation left fewer than two participants");
                }

                coreHub.NotifyVoiceChannelParticipants(dbChannel.PlanetId.Value,
                    new VoiceChannelParticipantsUpdate
                    {
                        PlanetId = dbChannel.PlanetId.Value,
                        ChannelId = channelId,
                        UserIds = remainingUserIds
                    });

                _logger.LogInformation(
                    "Cloudflare reconciliation removed {Count} stale participants from channel {ChannelId}",
                    staleUserIds.Count, channelId);

                // If no live session and no remaining participants, clean up the meeting mapping
                if (sessionsResult.Data!.Count == 0 && remainingUserIds.Count == 0)
                {
                    _realtimeKitService.RemoveMeetingMapping(channelId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reconciling channel {ChannelId} with Cloudflare", channelId);
            }
        }

        await CloseUnderpopulatedRealtimeKitSessionsAsync();
    }

    private async Task CloseUnderpopulatedRealtimeKitSessionsAsync()
    {
        var sessionsResult = await _realtimeKitService.GetLiveSessionsAsync();
        if (!sessionsResult.Success || sessionsResult.Data is null)
            return;

        foreach (var session in sessionsResult.Data)
        {
            if (session is null ||
                string.IsNullOrWhiteSpace(session.AssociatedId) ||
                session.LiveParticipants >= MinimumRealtimeKitParticipants ||
                !IsPastOrphanSessionGracePeriod(session))
            {
                continue;
            }

            await _realtimeKitService.CloseMeetingAsync(
                session.AssociatedId,
                "Cloudflare live session had fewer than two participants");
        }
    }

    private static bool IsPastOrphanSessionGracePeriod(RealtimeKitService.CloudflareSessionInfo session)
    {
        if (!DateTimeOffset.TryParse(session.CreatedAt, out var createdAt))
            return true;

        return DateTimeOffset.UtcNow - createdAt.ToUniversalTime() >= OrphanSessionGracePeriod;
    }

    private async Task CleanupTrackedRealtimeKitMeetingsAsync(string reason)
    {
        var trackedMeetings = await _realtimeKitService.LoadOpenMeetingMappingsAsync();
        var participantCountsByChannel = await GetRedisParticipantCountsByChannelAsync();
        var checkedMeetings = 0;
        var keptMeetings = 0;
        var closedMeetings = 0;
        var failedMeetings = 0;

        foreach (var (channelId, meetingId) in trackedMeetings)
        {
            if (string.IsNullOrWhiteSpace(meetingId))
                continue;

            checkedMeetings++;
            if (participantCountsByChannel.TryGetValue(channelId, out var participantCount) &&
                participantCount >= MinimumRealtimeKitParticipants)
            {
                keptMeetings++;
                continue;
            }

            var closeResult = await _realtimeKitService.CloseMeetingAsync(
                meetingId,
                $"{reason} found tracked Valour voice channel with fewer than two participants",
                channelId);

            if (closeResult.Success)
            {
                _realtimeKitService.RemoveMeetingMapping(channelId);
                closedMeetings++;
            }
            else
            {
                failedMeetings++;
            }
        }

        _logger.LogInformation(
            "RealtimeKit {Reason} checked {Checked} tracked meetings from the database; kept {Kept}, closed {Closed}, failed {Failed}",
            reason,
            checkedMeetings,
            keptMeetings,
            closedMeetings,
            failedMeetings);
    }

    private async Task<Dictionary<long, int>> GetRedisParticipantCountsByChannelAsync()
    {
        var result = new Dictionary<long, int>();

        try
        {
            var db = _redis.GetDatabase(RedisDbTypes.Cluster);
            var servers = _redis.GetServers();
            if (servers.Length == 0)
                return result;

            var channelKeys = servers
                .SelectMany(server => server.Keys(RedisDbTypes.Cluster, "voice:channel:*"))
                .Distinct()
                .ToList();

            foreach (var channelKey in channelKeys)
            {
                var channelIdStr = channelKey.ToString().Replace("voice:channel:", "");
                if (!long.TryParse(channelIdStr, out var channelId))
                    continue;

                var members = await db.SetMembersAsync(channelKey);
                var count = 0;

                foreach (var member in members)
                {
                    if (!long.TryParse((string?)member, out var userId))
                        continue;

                    var userChannel = await db.StringGetAsync($"voice:user:{userId}");
                    if (userChannel.HasValue &&
                        long.TryParse((string?)userChannel, out var currentChannelId) &&
                        currentChannelId == channelId)
                    {
                        count++;
                    }
                }

                result[channelId] = count;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not read Redis voice participant counts for RealtimeKit cleanup; treating all active meetings as unused");
        }


        return result;
    }
}
