using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Valour.Config.Configs;
using Valour.Shared;
using Valour.Shared.Models;
using DbRealtimeKitMeeting = Valour.Database.RealtimeKitMeeting;

namespace Valour.Server.Services;

public class RealtimeKitService : IVoiceProvider
{
    private const string CloudflareApiBase = "https://api.cloudflare.com/client/v4";
    private const int MinimumSessionKeepAliveSeconds = 60;
    private const string MeetingStatusActive = "ACTIVE";
    private const string MeetingStatusInactive = "INACTIVE";
    private static readonly TimeSpan OrphanSessionGracePeriod = TimeSpan.FromSeconds(90);

    public VoiceProvider Kind => VoiceProvider.RealtimeKit;

    // Explicit: exposes the private static config gate through the interface without
    // disturbing the many internal `if (!IsConfigured)` guards below.
    bool IVoiceProvider.IsConfigured => IsConfigured;

    IReadOnlyDictionary<long, string> IVoiceProvider.GetTrackedChannelMeetingIds() =>
        GetTrackedChannelMeetingIds();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RealtimeKitService> _logger;
    private readonly IServiceProvider _serviceProvider;

    private readonly ConcurrentDictionary<long, string> _meetingIdsByChannel = new();
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _channelLocks = new();
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _userJoinLocks = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RealtimeKitService(
        IHttpClientFactory httpClientFactory,
        ILogger<RealtimeKitService> logger,
        IServiceProvider serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    private static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CloudflareConfig.Instance?.RealtimeAccountId) &&
        !string.IsNullOrWhiteSpace(CloudflareConfig.Instance?.RealtimeAppId) &&
        !string.IsNullOrWhiteSpace(CloudflareConfig.Instance?.RealtimeApiToken);

    private static string VoicePresetName =>
        string.IsNullOrWhiteSpace(CloudflareConfig.Instance?.RealtimePresetName)
            ? "group_call_host"
            : CloudflareConfig.Instance.RealtimePresetName;

    private static string VideoPresetName =>
        string.IsNullOrWhiteSpace(CloudflareConfig.Instance?.RealtimeVideoPresetName)
            ? "video_call"
            : CloudflareConfig.Instance.RealtimeVideoPresetName;

    public async Task<TaskResult<RealtimeKitVoiceTokenResponse>> CreateParticipantTokenAsync(
        Channel channel,
        long userId,
        string displayName,
        string? sessionId)
    {
        if (!IsConfigured)
        {
            return TaskResult<RealtimeKitVoiceTokenResponse>.FromFailure(
                "RealtimeKit is not configured on the server.");
        }

        var userJoinLock = _userJoinLocks.GetOrAdd(userId, static _ => new SemaphoreSlim(1, 1));
        await userJoinLock.WaitAsync();

        try
        {
            var meetingResult = await GetOrCreateMeetingIdAsync(channel);
            if (!meetingResult.Success)
                return TaskResult<RealtimeKitVoiceTokenResponse>.FromFailure(meetingResult);

            // Always kick any existing participant sessions for this user in the same meeting before adding a new one.
            await KickUserFromMeetingAsync(meetingResult.Data, userId);

            var customParticipantId = BuildCustomParticipantId(userId, sessionId);

            var participantResult = await AddParticipantAsync(
                meetingResult.Data,
                customParticipantId,
                displayName,
                BuildParticipantMetadata(channel.Id, userId, sessionId),
                channel.ChannelType);

            if (!participantResult.Success)
                return TaskResult<RealtimeKitVoiceTokenResponse>.FromFailure(participantResult);

            return TaskResult<RealtimeKitVoiceTokenResponse>.FromData(new RealtimeKitVoiceTokenResponse
            {
                MeetingId = meetingResult.Data,
                ParticipantId = participantResult.Data.ParticipantId,
                AuthToken = participantResult.Data.AuthToken
            });
        }
        finally
        {
            userJoinLock.Release();
        }
    }

    private async Task<TaskResult<string>> GetOrCreateMeetingIdAsync(Channel channel)
    {
        if (_meetingIdsByChannel.TryGetValue(channel.Id, out var existingId))
        {
            await TouchTrackedMeetingAsync(channel.Id, existingId);
            return TaskResult<string>.FromData(existingId);
        }

        var trackedMeetingId = await GetOpenMeetingIdForChannelAsync(channel.Id);
        if (!string.IsNullOrWhiteSpace(trackedMeetingId))
        {
            _meetingIdsByChannel[channel.Id] = trackedMeetingId;
            return TaskResult<string>.FromData(trackedMeetingId);
        }

        var channelLock = _channelLocks.GetOrAdd(channel.Id, static _ => new SemaphoreSlim(1, 1));
        await channelLock.WaitAsync();

        try
        {
            if (_meetingIdsByChannel.TryGetValue(channel.Id, out existingId))
            {
                await TouchTrackedMeetingAsync(channel.Id, existingId);
                return TaskResult<string>.FromData(existingId);
            }

            trackedMeetingId = await GetOpenMeetingIdForChannelAsync(channel.Id);
            if (!string.IsNullOrWhiteSpace(trackedMeetingId))
            {
                _meetingIdsByChannel[channel.Id] = trackedMeetingId;
                return TaskResult<string>.FromData(trackedMeetingId);
            }

            var createResult = await CreateMeetingAsync(channel);
            if (!createResult.Success)
                return createResult;

            _meetingIdsByChannel[channel.Id] = createResult.Data;
            await TrackMeetingMappingAsync(channel.Id, createResult.Data, channel.PlanetId);
            return createResult;
        }
        finally
        {
            channelLock.Release();
        }
    }

    private async Task<TaskResult<string>> CreateMeetingAsync(Channel channel)
    {
        var endpoint = BuildEndpoint("meetings");
        var payload = new CreateMeetingRequest
        {
            Title = $"{channel.Name} ({channel.Id})",
            Metadata = $"channel:{channel.Id}",
            SessionKeepAliveTimeInSecs = MinimumSessionKeepAliveSeconds,
            Status = MeetingStatusActive,
            RecordOnStart = false,
            LiveStreamOnStart = false,
            PersistChat = false,
            SummarizeOnEnd = false
        };

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", CloudflareConfig.Instance.RealtimeApiToken);

        return await SendAsync<CloudflareMeetingResult, string>(
            request,
            data => data.Id,
            "create meeting");
    }

    private async Task<TaskResult<ParticipantTokenResult>> AddParticipantAsync(
        string meetingId,
        string customParticipantId,
        string displayName,
        string metadata,
        ChannelTypeEnum channelType)
    {
        var endpoint = BuildEndpoint($"meetings/{meetingId}/participants");
        var presetName = channelType == ChannelTypeEnum.PlanetVideo ? VideoPresetName : VoicePresetName;
        var payload = new AddParticipantRequest
        {
            PresetName = presetName,
            CustomParticipantId = customParticipantId,
            Name = displayName,
            Metadata = metadata
        };

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", CloudflareConfig.Instance.RealtimeApiToken);

        return await SendAsync<CloudflareParticipantResult, ParticipantTokenResult>(
            request,
            data => new ParticipantTokenResult
            {
                ParticipantId = data.Id,
                AuthToken = data.Token
            },
            "add participant");
    }

    private static string BuildCustomParticipantId(long userId, string? sessionId)
    {
        var normalizedSessionId = NormalizeSessionId(sessionId);
        return string.IsNullOrWhiteSpace(normalizedSessionId)
            ? userId.ToString()
            : $"{userId}:{normalizedSessionId}";
    }

    private static string NormalizeSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return string.Empty;

        // Preserve a stable userId:sessionId format for downstream parsing.
        return sessionId.Trim().Replace(':', '_');
    }

    private static string BuildParticipantMetadata(long channelId, long userId, string? sessionId)
    {
        var normalizedSessionId = NormalizeSessionId(sessionId);
        return string.IsNullOrWhiteSpace(normalizedSessionId)
            ? $"{{\"channelId\":\"{channelId}\",\"userId\":\"{userId}\"}}"
            : $"{{\"channelId\":\"{channelId}\",\"userId\":\"{userId}\",\"sessionId\":\"{normalizedSessionId}\"}}";
    }

    /// <summary>
    /// Best-effort cleanup: kicks all RTK sessions for a user in a tracked channel.
    /// </summary>
    public async Task KickUserFromTrackedChannelAsync(long channelId, long userId)
    {
        if (!_meetingIdsByChannel.TryGetValue(channelId, out var meetingId) || string.IsNullOrWhiteSpace(meetingId))
            return;

        await KickUserFromMeetingAsync(meetingId, userId);
    }

    /// <summary>
    /// Best-effort cleanup: kicks the exact RTK session for a user when a sessionId is known.
    /// Falls back to kicking all user sessions in the channel when no sessionId is provided.
    /// </summary>
    public async Task KickUserSessionFromTrackedChannelAsync(long channelId, long userId, string? sessionId)
    {
        if (!_meetingIdsByChannel.TryGetValue(channelId, out var meetingId) || string.IsNullOrWhiteSpace(meetingId))
            return;

        var normalizedSessionId = NormalizeSessionId(sessionId);
        if (string.IsNullOrWhiteSpace(normalizedSessionId))
        {
            await KickUserFromMeetingAsync(meetingId, userId);
            return;
        }

        await KickParticipantFromMeetingAsync(meetingId, BuildCustomParticipantId(userId, normalizedSessionId));
    }

    /// <summary>
    /// Kicks all active RTK participants for the specified user from a meeting (across all live sessions).
    /// Failures are logged but not thrown — this is best-effort cleanup.
    /// </summary>
    private async Task KickUserFromMeetingAsync(string meetingId, long userId)
    {
        try
        {
            var sessionsResult = await GetLiveSessionsForMeetingAsync(meetingId);
            if (!sessionsResult.Success || sessionsResult.Data is null)
                return;

            var customParticipantIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var session in sessionsResult.Data)
            {
                if (session is null || string.IsNullOrWhiteSpace(session.Id) || session.LiveParticipants <= 0)
                    continue;

                var participantsResult = await GetSessionParticipantsAsync(session.Id);
                if (!participantsResult.Success || participantsResult.Data is null)
                    continue;

                foreach (var participant in participantsResult.Data)
                {
                    if (participant.ExtractUserId() != userId)
                        continue;

                    if (!string.IsNullOrWhiteSpace(participant.CustomParticipantId))
                        customParticipantIds.Add(participant.CustomParticipantId);
                }
            }

            if (customParticipantIds.Count == 0)
                return;

            await KickParticipantsFromMeetingAsync(meetingId, customParticipantIds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to kick existing participant sessions for user {UserId} from meeting {MeetingId}",
                userId,
                meetingId);
        }
    }

    /// <summary>
    /// Kicks one or more participants from the active session of a meeting via the Cloudflare API.
    /// Failures are logged but not thrown — this is best-effort cleanup.
    /// </summary>
    private async Task KickParticipantsFromMeetingAsync(string meetingId, IEnumerable<string> customParticipantIds)
    {
        var ids = customParticipantIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
            return;

        try
        {
            var endpoint = BuildEndpoint($"meetings/{meetingId}/active-session/kick");
            var payload = new KickParticipantRequest
            {
                CustomParticipantIds = ids
            };

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload)
            };

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", CloudflareConfig.Instance.RealtimeApiToken);

            using var client = _httpClientFactory.CreateClient();
            using var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogDebug(
                    "Kick participants {ParticipantIds} from meeting {MeetingId} returned {Status}: {Body}",
                    string.Join(", ", ids), meetingId, (int)response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kick participants from meeting {MeetingId}: {ParticipantIds}",
                meetingId, string.Join(", ", ids));
        }
    }

    private Task KickParticipantFromMeetingAsync(string meetingId, string customParticipantId)
    {
        return KickParticipantsFromMeetingAsync(meetingId, new[] { customParticipantId });
    }

    /// <summary>
    /// Returns a snapshot of all channel → meeting ID mappings currently tracked.
    /// </summary>
    public Dictionary<long, string> GetTrackedChannelMeetingIds()
    {
        return new Dictionary<long, string>(_meetingIdsByChannel);
    }

    /// <summary>
    /// The backend's authoritative connected-user set for a meeting: unions the
    /// live sessions' participants (excluding those that have left). Returns null
    /// when Cloudflare could not be queried, so the caller skips reconciliation
    /// rather than removing live participants.
    /// </summary>
    public async Task<HashSet<long>?> GetConnectedUserIdsAsync(long channelId, string meetingId)
    {
        var sessionsResult = await GetLiveSessionsForMeetingAsync(meetingId);
        if (!sessionsResult.Success || sessionsResult.Data is null)
            return null;

        var userIds = new HashSet<long>();
        foreach (var session in sessionsResult.Data)
        {
            if (session is null || string.IsNullOrWhiteSpace(session.Id))
                continue;

            var participantsResult = await GetSessionParticipantsAsync(session.Id);
            if (!participantsResult.Success || participantsResult.Data is null)
                continue;

            foreach (var participant in participantsResult.Data)
            {
                if (!string.IsNullOrEmpty(participant.LeftAt))
                    continue;

                var userId = participant.ExtractUserId();
                if (userId.HasValue)
                    userIds.Add(userId.Value);
            }
        }

        return userIds;
    }

    /// <summary>
    /// Sweeps app-wide live sessions and closes any that have sat below the
    /// participant threshold past the orphan grace period — a backstop for
    /// server-side meetings the tracked map no longer knows about.
    /// </summary>
    public async Task CloseOrphanedSessionsAsync(int minParticipants)
    {
        var sessionsResult = await GetLiveSessionsAsync();
        if (!sessionsResult.Success || sessionsResult.Data is null)
            return;

        foreach (var session in sessionsResult.Data)
        {
            if (session is null ||
                string.IsNullOrWhiteSpace(session.AssociatedId) ||
                session.LiveParticipants >= minParticipants ||
                !IsPastOrphanSessionGracePeriod(session))
            {
                continue;
            }

            await CloseMeetingAsync(
                session.AssociatedId,
                "Cloudflare live session had fewer than the minimum participants");
        }
    }

    private static bool IsPastOrphanSessionGracePeriod(CloudflareSessionInfo session)
    {
        if (!DateTimeOffset.TryParse(session.CreatedAt, out var createdAt))
            return true;

        return DateTimeOffset.UtcNow - createdAt.ToUniversalTime() >= OrphanSessionGracePeriod;
    }

    public async Task<Dictionary<long, string>> LoadOpenMeetingMappingsAsync()
    {
        if (!IsConfigured)
            return new Dictionary<long, string>();

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();

            var records = await db.RealtimeKitMeetings
                .AsNoTracking()
                .Where(x => x.ClosedAt == null && x.Status == MeetingStatusActive)
                .ToListAsync();

            foreach (var record in records)
            {
                if (!string.IsNullOrWhiteSpace(record.MeetingId))
                    _meetingIdsByChannel[record.ChannelId] = record.MeetingId;
            }

            return records
                .Where(x => !string.IsNullOrWhiteSpace(x.MeetingId))
                .ToDictionary(x => x.ChannelId, x => x.MeetingId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load RealtimeKit meeting mappings from the database");
            return new Dictionary<long, string>();
        }
    }

    /// <summary>
    /// Removes a channel → meeting ID mapping (e.g. when the meeting is no longer active).
    /// </summary>
    public void RemoveMeetingMapping(long channelId)
    {
        _meetingIdsByChannel.TryRemove(channelId, out _);
    }

    public void TrackMeetingMapping(long channelId, string meetingId)
    {
        if (string.IsNullOrWhiteSpace(meetingId))
            return;

        _meetingIdsByChannel[channelId] = meetingId;
    }

    public async Task TrackMeetingMappingAsync(long channelId, string meetingId, long? planetId = null)
    {
        if (string.IsNullOrWhiteSpace(meetingId))
            return;

        _meetingIdsByChannel[channelId] = meetingId;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var now = DateTime.UtcNow;

            var existing = await db.RealtimeKitMeetings
                .FirstOrDefaultAsync(x => x.MeetingId == meetingId || (x.ChannelId == channelId && x.ClosedAt == null));

            if (existing is null)
            {
                db.RealtimeKitMeetings.Add(new DbRealtimeKitMeeting
                {
                    ChannelId = channelId,
                    PlanetId = planetId,
                    MeetingId = meetingId,
                    Status = MeetingStatusActive,
                    CreatedAt = now,
                    LastUsedAt = now,
                    LastCleanupError = string.Empty
                });
            }
            else
            {
                existing.ChannelId = channelId;
                existing.PlanetId ??= planetId;
                existing.MeetingId = meetingId;
                existing.Status = MeetingStatusActive;
                existing.LastUsedAt = now;
                existing.ClosedAt = null;
                existing.LastCleanupError = string.Empty;
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist RealtimeKit meeting {MeetingId} for channel {ChannelId}",
                meetingId,
                channelId);
        }
    }

    /// <summary>
    /// Best-effort shutdown for a tracked meeting. Kicks any remaining RTK peers, marks the
    /// meeting inactive so old tokens cannot rejoin it, then drops the local mapping.
    /// </summary>
    public async Task CloseTrackedMeetingAsync(long channelId, string reason)
    {
        if (!IsConfigured)
            return;

        if (!_meetingIdsByChannel.TryGetValue(channelId, out var meetingId) ||
            string.IsNullOrWhiteSpace(meetingId))
        {
            meetingId = await GetOpenMeetingIdForChannelAsync(channelId);
            if (string.IsNullOrWhiteSpace(meetingId))
                return;

            _meetingIdsByChannel[channelId] = meetingId;
        }

        var channelLock = _channelLocks.GetOrAdd(channelId, static _ => new SemaphoreSlim(1, 1));
        await channelLock.WaitAsync();

        try
        {
            if (!_meetingIdsByChannel.TryGetValue(channelId, out meetingId) ||
                string.IsNullOrWhiteSpace(meetingId))
            {
                meetingId = await GetOpenMeetingIdForChannelAsync(channelId);
                if (string.IsNullOrWhiteSpace(meetingId))
                    return;
            }

            var closeResult = await CloseMeetingAsync(
                meetingId,
                reason,
                channelId);

            if (closeResult.Success)
            {
                _meetingIdsByChannel.TryRemove(channelId, out _);
                await MarkMeetingClosedAsync(meetingId);
            }
            else
            {
                await RecordMeetingCleanupFailureAsync(meetingId, closeResult.Message);
            }
        }
        finally
        {
            channelLock.Release();
        }
    }

    public async Task<TaskResult> CloseMeetingAsync(string meetingId, string reason, long? channelId = null)
    {
        if (!IsConfigured)
            return TaskResult.FromFailure("RealtimeKit is not configured.");

        if (string.IsNullOrWhiteSpace(meetingId))
            return TaskResult.FromFailure("Meeting id is required.");

        var kickResult = await KickAllParticipantsFromMeetingAsync(meetingId);
        var inactiveResult = await SetMeetingInactiveAsync(meetingId);

        if (!inactiveResult.Success)
        {
            await RecordMeetingCleanupFailureAsync(meetingId, inactiveResult.Message);
            _logger.LogWarning(
                "Cloudflare cleanup failed to mark RTK meeting {MeetingId} inactive in channel {ChannelId}. Inactive: {InactiveMessage}. Kick: {KickMessage}. Reason: {Reason}",
                meetingId,
                channelId,
                inactiveResult.Message,
                kickResult.Message,
                reason);

            return TaskResult.FromFailure("Cloudflare cleanup was incomplete.");
        }

        await MarkMeetingClosedAsync(meetingId);

        if (!kickResult.Success)
        {
            _logger.LogInformation(
                "Marked RTK meeting {MeetingId} inactive, but kick-all did not complete. Channel: {ChannelId}. Kick: {KickMessage}. Reason: {Reason}",
                meetingId,
                channelId,
                kickResult.Message,
                reason);
        }

        _logger.LogInformation(
            "Closed RTK meeting {MeetingId} for channel {ChannelId}. Reason: {Reason}",
            meetingId,
            channelId,
            reason);

        return TaskResult.SuccessResult;
    }

    private async Task<TaskResult> KickAllParticipantsFromMeetingAsync(string meetingId)
    {
        var endpoint = BuildEndpoint($"meetings/{meetingId}/active-session/kick-all");
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { })
        };

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", CloudflareConfig.Instance.RealtimeApiToken);

        return await SendCommandAsync(request, "kick all participants");
    }

    private async Task<TaskResult> SetMeetingInactiveAsync(string meetingId)
    {
        var endpoint = BuildEndpoint($"meetings/{meetingId}");
        var payload = new UpdateMeetingRequest
        {
            Status = MeetingStatusInactive
        };

        var request = new HttpRequestMessage(HttpMethod.Patch, endpoint)
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", CloudflareConfig.Instance.RealtimeApiToken);

        return await SendCommandAsync(request, "mark meeting inactive");
    }

    private async Task<string> GetOpenMeetingIdForChannelAsync(long channelId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();

            var record = await db.RealtimeKitMeetings
                .AsNoTracking()
                .Where(x => x.ChannelId == channelId && x.ClosedAt == null && x.Status == MeetingStatusActive)
                .OrderByDescending(x => x.LastUsedAt)
                .FirstOrDefaultAsync();

            if (record is null || string.IsNullOrWhiteSpace(record.MeetingId))
                return string.Empty;

            await TouchTrackedMeetingAsync(channelId, record.MeetingId);
            return record.MeetingId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load RealtimeKit meeting for channel {ChannelId}", channelId);
            return string.Empty;
        }
    }

    private async Task TouchTrackedMeetingAsync(long channelId, string meetingId)
    {
        if (string.IsNullOrWhiteSpace(meetingId))
            return;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();

            var record = await db.RealtimeKitMeetings
                .FirstOrDefaultAsync(x => x.MeetingId == meetingId || (x.ChannelId == channelId && x.ClosedAt == null));

            if (record is null)
                return;

            record.LastUsedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to touch RealtimeKit meeting {MeetingId}", meetingId);
        }
    }

    private async Task MarkMeetingClosedAsync(string meetingId)
    {
        if (string.IsNullOrWhiteSpace(meetingId))
            return;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();

            var record = await db.RealtimeKitMeetings
                .FirstOrDefaultAsync(x => x.MeetingId == meetingId);

            if (record is null)
                return;

            var now = DateTime.UtcNow;
            record.Status = MeetingStatusInactive;
            record.ClosedAt = now;
            record.LastCleanupAttemptAt = now;
            record.LastCleanupError = string.Empty;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark RealtimeKit meeting {MeetingId} closed in the database", meetingId);
        }
    }

    private async Task RecordMeetingCleanupFailureAsync(string meetingId, string error)
    {
        if (string.IsNullOrWhiteSpace(meetingId))
            return;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();

            var record = await db.RealtimeKitMeetings
                .FirstOrDefaultAsync(x => x.MeetingId == meetingId);

            if (record is null)
                return;

            record.LastCleanupAttemptAt = DateTime.UtcNow;
            record.CleanupFailureCount++;
            record.LastCleanupError = string.IsNullOrWhiteSpace(error) ? "Unknown cleanup failure" : error;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record RealtimeKit meeting cleanup failure for {MeetingId}", meetingId);
        }
    }

    /// <summary>
    /// Fetches all LIVE sessions for a specific meeting from Cloudflare.
    /// </summary>
    public async Task<TaskResult<List<CloudflareSessionInfo>>> GetLiveSessionsForMeetingAsync(string meetingId)
    {
        if (!IsConfigured)
            return TaskResult<List<CloudflareSessionInfo>>.FromFailure("RealtimeKit is not configured.");

        var endpoint = BuildEndpoint($"sessions?associated_id={meetingId}&status=LIVE&per_page=100");
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", CloudflareConfig.Instance.RealtimeApiToken);

        return await SendAsync<CloudflareSessionsListResult, List<CloudflareSessionInfo>>(
            request,
            data => data.Sessions ?? new List<CloudflareSessionInfo>(),
            "list live sessions");
    }

    /// <summary>
    /// Fetches all LIVE sessions for the RealtimeKit app.
    /// </summary>
    public async Task<TaskResult<List<CloudflareSessionInfo>>> GetLiveSessionsAsync()
    {
        if (!IsConfigured)
            return TaskResult<List<CloudflareSessionInfo>>.FromFailure("RealtimeKit is not configured.");

        var endpoint = BuildEndpoint("sessions?status=LIVE&per_page=100");
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", CloudflareConfig.Instance.RealtimeApiToken);

        return await SendAsync<CloudflareSessionsListResult, List<CloudflareSessionInfo>>(
            request,
            data => data.Sessions ?? new List<CloudflareSessionInfo>(),
            "list app live sessions");
    }

    /// <summary>
    /// Fetches all active-or-unknown meetings for the RealtimeKit app.
    /// </summary>
    public async Task<TaskResult<List<CloudflareMeetingInfo>>> GetActiveMeetingsAsync()
    {
        var meetingsResult = await GetMeetingsAsync();
        if (!meetingsResult.Success || meetingsResult.Data is null)
            return TaskResult<List<CloudflareMeetingInfo>>.FromFailure(meetingsResult);

        var activeMeetings = meetingsResult.Data
            .Where(static meeting => meeting.IsActiveOrUnknown())
            .ToList();

        return TaskResult<List<CloudflareMeetingInfo>>.FromData(activeMeetings);
    }

    /// <summary>
    /// Fetches all meetings for the RealtimeKit app.
    /// </summary>
    public async Task<TaskResult<List<CloudflareMeetingInfo>>> GetMeetingsAsync()
    {
        if (!IsConfigured)
            return TaskResult<List<CloudflareMeetingInfo>>.FromFailure("RealtimeKit is not configured.");

        const int pageSize = 30;
        var meetings = new List<CloudflareMeetingInfo>();
        var seenMeetingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var page = 1; page <= 100; page++)
        {
            var endpoint = BuildEndpoint($"meetings?per_page={pageSize}&page_no={page}");
            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", CloudflareConfig.Instance.RealtimeApiToken);

            var pageResult = await SendWrapperAsync<List<CloudflareMeetingInfo>>(
                request,
                $"list meetings page {page}");

            if (!pageResult.Success || pageResult.Data is null)
            {
                if (page == 1)
                    return await GetMeetingsWithoutPagingAsync();

                return TaskResult<List<CloudflareMeetingInfo>>.FromFailure(pageResult);
            }

            var pageMeetings = pageResult.Data.Result ?? new List<CloudflareMeetingInfo>();
            var addedCount = 0;

            foreach (var meeting in pageMeetings)
            {
                if (meeting is null ||
                    string.IsNullOrWhiteSpace(meeting.Id) ||
                    !seenMeetingIds.Add(meeting.Id))
                {
                    continue;
                }

                meetings.Add(meeting);
                addedCount++;
            }

            var totalCount = pageResult.Data.Paging?.TotalCount;
            if (pageMeetings.Count == 0 ||
                addedCount == 0 ||
                pageMeetings.Count < pageSize ||
                (totalCount.HasValue && meetings.Count >= totalCount.Value))
            {
                break;
            }
        }

        return TaskResult<List<CloudflareMeetingInfo>>.FromData(meetings);
    }

    private async Task<TaskResult<List<CloudflareMeetingInfo>>> GetMeetingsWithoutPagingAsync()
    {
        _logger.LogWarning(
            "Cloudflare RealtimeKit paged meeting list failed; retrying with the default unpaged meeting list");

        var request = new HttpRequestMessage(HttpMethod.Get, BuildEndpoint("meetings"));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", CloudflareConfig.Instance.RealtimeApiToken);

        var result = await SendWrapperAsync<List<CloudflareMeetingInfo>>(
            request,
            "list meetings without paging");

        if (!result.Success || result.Data is null)
            return TaskResult<List<CloudflareMeetingInfo>>.FromFailure(result);

        return TaskResult<List<CloudflareMeetingInfo>>.FromData(
            result.Data.Result ?? new List<CloudflareMeetingInfo>());
    }

    /// <summary>
    /// Fetches all participants for a specific session from Cloudflare.
    /// </summary>
    public async Task<TaskResult<List<CloudflareSessionParticipantInfo>>> GetSessionParticipantsAsync(string sessionId)
    {
        if (!IsConfigured)
            return TaskResult<List<CloudflareSessionParticipantInfo>>.FromFailure("RealtimeKit is not configured.");

        var endpoint = BuildEndpoint($"sessions/{sessionId}/participants?per_page=500");
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", CloudflareConfig.Instance.RealtimeApiToken);

        return await SendAsync<CloudflareSessionParticipantsResult, List<CloudflareSessionParticipantInfo>>(
            request,
            data => data.Participants ?? new List<CloudflareSessionParticipantInfo>(),
            "list session participants");
    }

    private async Task<TaskResult<TOut>> SendAsync<TCloudflare, TOut>(
        HttpRequestMessage request,
        Func<TCloudflare, TOut> map,
        string operation)
        where TCloudflare : class
    {
        try
        {
            using (request)
            {
                using var client = _httpClientFactory.CreateClient();
                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                CloudflareResponse<TCloudflare>? wrapper = null;

                if (!string.IsNullOrWhiteSpace(body))
                {
                    wrapper = JsonSerializer.Deserialize<CloudflareResponse<TCloudflare>>(body, JsonOptions);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var message = GetErrorMessage(wrapper, body);
                    _logger.LogWarning(
                        "Cloudflare RealtimeKit failed to {Operation}. Status: {Status}. Message: {Message}",
                        operation,
                        (int)response.StatusCode,
                        message);

                    return TaskResult<TOut>.FromFailure($"Failed to {operation}: {message}", (int)response.StatusCode);
                }

                if (wrapper?.Success != true || wrapper.Result is null)
                {
                    var message = GetErrorMessage(wrapper, body);
                    return TaskResult<TOut>.FromFailure($"Failed to {operation}: {message}");
                }

                return TaskResult<TOut>.FromData(map(wrapper.Result));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloudflare RealtimeKit request failed while trying to {Operation}", operation);
            return TaskResult<TOut>.FromFailure(ex);
        }
    }

    private async Task<TaskResult> SendCommandAsync(HttpRequestMessage request, string operation)
    {
        try
        {
            using (request)
            {
                using var client = _httpClientFactory.CreateClient();
                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                CloudflareResponse<JsonElement>? wrapper = null;

                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        wrapper = JsonSerializer.Deserialize<CloudflareResponse<JsonElement>>(body, JsonOptions);
                    }
                    catch (JsonException)
                    {
                        // Some command endpoints may return an empty or non-standard body.
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    var message = GetErrorMessage(wrapper, body);
                    _logger.LogWarning(
                        "Cloudflare RealtimeKit failed to {Operation}. Status: {Status}. Message: {Message}",
                        operation,
                        (int)response.StatusCode,
                        message);

                    return TaskResult.FromFailure($"Failed to {operation}: {message}", (int)response.StatusCode);
                }

                if (wrapper is not null && !wrapper.Success)
                {
                    var message = GetErrorMessage(wrapper, body);
                    return TaskResult.FromFailure($"Failed to {operation}: {message}");
                }

                return TaskResult.SuccessResult;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloudflare RealtimeKit request failed while trying to {Operation}", operation);
            return TaskResult.FromFailure(ex);
        }
    }

    private async Task<TaskResult<CloudflareResponse<TCloudflare>>> SendWrapperAsync<TCloudflare>(
        HttpRequestMessage request,
        string operation)
        where TCloudflare : class
    {
        try
        {
            using (request)
            {
                using var client = _httpClientFactory.CreateClient();
                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                CloudflareResponse<TCloudflare>? wrapper = null;

                if (!string.IsNullOrWhiteSpace(body))
                {
                    wrapper = JsonSerializer.Deserialize<CloudflareResponse<TCloudflare>>(body, JsonOptions);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var message = GetErrorMessage(wrapper, body);
                    _logger.LogWarning(
                        "Cloudflare RealtimeKit failed to {Operation}. Status: {Status}. Message: {Message}",
                        operation,
                        (int)response.StatusCode,
                        message);

                    return TaskResult<CloudflareResponse<TCloudflare>>.FromFailure(
                        $"Failed to {operation}: {message}",
                        (int)response.StatusCode);
                }

                if (wrapper?.Success != true)
                {
                    var message = GetErrorMessage(wrapper, body);
                    return TaskResult<CloudflareResponse<TCloudflare>>.FromFailure($"Failed to {operation}: {message}");
                }

                return TaskResult<CloudflareResponse<TCloudflare>>.FromData(wrapper);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloudflare RealtimeKit request failed while trying to {Operation}", operation);
            return TaskResult<CloudflareResponse<TCloudflare>>.FromFailure(ex);
        }
    }

    private static string BuildEndpoint(string relativePath)
    {
        return
            $"{CloudflareApiBase}/accounts/{CloudflareConfig.Instance.RealtimeAccountId}/realtime/kit/{CloudflareConfig.Instance.RealtimeAppId}/{relativePath}";
    }

    private static string GetErrorMessage<T>(CloudflareResponse<T>? response, string rawBody)
    {
        var cloudflareError = response?.Errors?.FirstOrDefault();
        if (cloudflareError is not null)
        {
            return $"{cloudflareError.Code}: {cloudflareError.Message}";
        }

        return string.IsNullOrWhiteSpace(rawBody) ? "Unknown error" : rawBody;
    }

    private sealed class CreateMeetingRequest
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("metadata")]
        public string Metadata { get; set; } = string.Empty;

        [JsonPropertyName("session_keep_alive_time_in_secs")]
        public int SessionKeepAliveTimeInSecs { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = MeetingStatusActive;

        [JsonPropertyName("record_on_start")]
        public bool RecordOnStart { get; set; }

        [JsonPropertyName("live_stream_on_start")]
        public bool LiveStreamOnStart { get; set; }

        [JsonPropertyName("persist_chat")]
        public bool PersistChat { get; set; }

        [JsonPropertyName("summarize_on_end")]
        public bool SummarizeOnEnd { get; set; }
    }

    private sealed class UpdateMeetingRequest
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = MeetingStatusInactive;
    }

    private sealed class AddParticipantRequest
    {
        [JsonPropertyName("preset_name")]
        public string PresetName { get; set; } = string.Empty;

        [JsonPropertyName("custom_participant_id")]
        public string CustomParticipantId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("metadata")]
        public string Metadata { get; set; } = string.Empty;
    }

    private sealed class KickParticipantRequest
    {
        [JsonPropertyName("custom_participant_ids")]
        public string[] CustomParticipantIds { get; set; } = Array.Empty<string>();
    }

    private sealed class CloudflareMeetingResult
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    public sealed class CloudflareMeetingInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("metadata")]
        public JsonElement Metadata { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        public bool IsActiveOrUnknown()
        {
            return !string.Equals(Status, MeetingStatusInactive, StringComparison.OrdinalIgnoreCase);
        }

        public long? ExtractChannelId()
        {
            var metadataText = GetMetadataText();
            if (!string.IsNullOrWhiteSpace(metadataText) &&
                metadataText.StartsWith("channel:", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(metadataText["channel:".Length..], out var metadataChannelId))
            {
                return metadataChannelId;
            }

            var openIndex = Title.LastIndexOf('(');
            var closeIndex = Title.LastIndexOf(')');
            if (openIndex >= 0 &&
                closeIndex > openIndex &&
                long.TryParse(Title[(openIndex + 1)..closeIndex], out var titleChannelId))
            {
                return titleChannelId;
            }

            return null;
        }

        private string? GetMetadataText()
        {
            if (Metadata.ValueKind == JsonValueKind.String)
                return Metadata.GetString();

            if (Metadata.ValueKind != JsonValueKind.Object)
                return null;

            if (Metadata.TryGetProperty("channelId", out var channelId) ||
                Metadata.TryGetProperty("channel_id", out channelId) ||
                Metadata.TryGetProperty("channel", out channelId))
            {
                if (channelId.ValueKind == JsonValueKind.String)
                    return $"channel:{channelId.GetString()}";

                if (channelId.ValueKind == JsonValueKind.Number &&
                    channelId.TryGetInt64(out var numericChannelId))
                {
                    return $"channel:{numericChannelId}";
                }
            }

            return null;
        }
    }

    private sealed class CloudflareParticipantResult
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;
    }

    private sealed class ParticipantTokenResult
    {
        public string ParticipantId { get; set; } = string.Empty;
        public string AuthToken { get; set; } = string.Empty;
    }

    private sealed class CloudflareResponse<T>
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("data")]
        public T? Result { get; set; }

        [JsonPropertyName("paging")]
        public CloudflarePaging? Paging { get; set; }

        [JsonPropertyName("errors")]
        public CloudflareError[]? Errors { get; set; }
    }

    private sealed class CloudflarePaging
    {
        [JsonPropertyName("total_count")]
        public int TotalCount { get; set; }
    }

    private sealed class CloudflareError
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed class CloudflareSessionsListResult
    {
        [JsonPropertyName("sessions")]
        public List<CloudflareSessionInfo>? Sessions { get; set; }
    }

    private sealed class CloudflareSessionParticipantsResult
    {
        [JsonPropertyName("participants")]
        public List<CloudflareSessionParticipantInfo>? Participants { get; set; }
    }

    public sealed class CloudflareSessionInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("associated_id")]
        public string AssociatedId { get; set; } = string.Empty;

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("live_participants")]
        public int LiveParticipants { get; set; }
    }

    public sealed class CloudflareSessionParticipantInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("custom_participant_id")]
        public string CustomParticipantId { get; set; } = string.Empty;

        [JsonPropertyName("left_at")]
        public string? LeftAt { get; set; }

        public long? ExtractUserId()
        {
            if (string.IsNullOrEmpty(CustomParticipantId))
                return null;

            var delimiterIndex = CustomParticipantId.IndexOf(':');
            var candidate = delimiterIndex > 0 ? CustomParticipantId[..delimiterIndex] : CustomParticipantId;
            return long.TryParse(candidate, out var userId) ? userId : null;
        }
    }
}
