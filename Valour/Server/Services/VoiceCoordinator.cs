using System.Collections.Concurrent;
using Microsoft.AspNetCore.DataProtection;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// The voice backend the rest of the server talks to. Routes each channel to
/// either the planet's own bring-your-own-voice LiveKit SFU (when the planet
/// has an enabled config) or the instance-wide provider (RealtimeKit or
/// LiveKit). Registered as the <see cref="IVoiceProvider"/> singleton, so the
/// signalling API, cleanup worker, and manifest are unaware of the split.
/// </summary>
public class VoiceCoordinator : IVoiceProvider
{
    private static readonly TimeSpan ResolutionTtl = TimeSpan.FromMinutes(5);

    private readonly IVoiceProvider _instance;
    private readonly LiveKitService _liveKit;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDataProtector _protector;
    private readonly ILogger<VoiceCoordinator> _logger;

    // channelId -> BYO resolution. Positive entries carry credentials; negative
    // entries (Credentials=null) suppress repeat DB lookups for instance-backed
    // channels. TTL-bounded so config changes propagate across cluster nodes
    // that didn't observe the invalidation.
    private readonly ConcurrentDictionary<long, ChannelResolution> _resolutions = new();

    private sealed record ChannelResolution(long PlanetId, LiveKitCredentials? Credentials, DateTime ResolvedAt)
    {
        public bool Expired => DateTime.UtcNow - ResolvedAt > ResolutionTtl;
    }

    public VoiceCoordinator(
        IVoiceProvider instanceProvider,
        LiveKitService liveKit,
        IServiceProvider serviceProvider,
        IDataProtectionProvider dataProtection,
        ILogger<VoiceCoordinator> logger)
    {
        _instance = instanceProvider;
        _liveKit = liveKit;
        _serviceProvider = serviceProvider;
        _protector = dataProtection.CreateProtector(PlanetVoiceService.ProtectorPurpose);
        _logger = logger;
    }

    // Manifest semantics are instance-level: BYO planets don't change what this
    // deployment itself offers.
    public VoiceProvider Kind => _instance.Kind;
    public bool IsConfigured => _instance.IsConfigured;

    // ================= Routing =================

    public async Task<TaskResult<RealtimeKitVoiceTokenResponse>> CreateParticipantTokenAsync(
        Channel channel, long userId, string displayName, string? sessionId)
    {
        var creds = await ResolveAsync(channel.Id, channel.PlanetId);
        if (creds is null)
            return await _instance.CreateParticipantTokenAsync(channel, userId, displayName, sessionId);

        var response = _liveKit.CreateParticipantTokenWithCredentials(
            creds.Value, channel.Id, userId, displayName, sessionId);

        return TaskResult<RealtimeKitVoiceTokenResponse>.FromData(response);
    }

    public async Task KickUserFromTrackedChannelAsync(long channelId, long userId)
    {
        var creds = await ResolveAsync(channelId);
        if (creds is null)
            await _instance.KickUserFromTrackedChannelAsync(channelId, userId);
        else
            await _liveKit.KickUserWithCredentialsAsync(creds.Value, channelId, userId);
    }

    public async Task KickUserSessionFromTrackedChannelAsync(long channelId, long userId, string? sessionId)
    {
        var creds = await ResolveAsync(channelId);
        if (creds is null)
            await _instance.KickUserSessionFromTrackedChannelAsync(channelId, userId, sessionId);
        else
            await _liveKit.KickUserSessionWithCredentialsAsync(creds.Value, channelId, userId, sessionId);
    }

    public async Task CloseTrackedMeetingAsync(long channelId, string reason)
    {
        var creds = await ResolveAsync(channelId);
        if (creds is null)
            await _instance.CloseTrackedMeetingAsync(channelId, reason);
        else
            await _liveKit.CloseMeetingWithCredentialsAsync(
                creds.Value, LiveKitService.RoomName(channelId), reason, channelId);
    }

    public async Task<TaskResult> CloseMeetingAsync(string meetingId, string reason, long? channelId = null)
    {
        var creds = channelId is null ? null : await ResolveAsync(channelId.Value);
        return creds is null
            ? await _instance.CloseMeetingAsync(meetingId, reason, channelId)
            : await _liveKit.CloseMeetingWithCredentialsAsync(creds.Value, meetingId, reason, channelId);
    }

    public async Task<HashSet<long>?> GetConnectedUserIdsAsync(long channelId, string meetingId)
    {
        var creds = await ResolveAsync(channelId);
        return creds is null
            ? await _instance.GetConnectedUserIdsAsync(channelId, meetingId)
            : await _liveKit.GetConnectedUserIdsWithCredentialsAsync(creds.Value, meetingId);
    }

    // ================= Tracking (union of instance + BYO) =================

    public IReadOnlyDictionary<long, string> GetTrackedChannelMeetingIds()
    {
        var merged = new Dictionary<long, string>(_instance.GetTrackedChannelMeetingIds());
        foreach (var (channelId, resolution) in _resolutions)
        {
            if (resolution.Credentials is not null)
                merged[channelId] = LiveKitService.RoomName(channelId);
        }

        return merged;
    }

    public async Task<Dictionary<long, string>> LoadOpenMeetingMappingsAsync()
    {
        // BYO rooms aren't enumerated across every owner SFU — they auto-expire
        // via empty_timeout, and the ones live in this process are already in the
        // resolution registry (covered by GetTrackedChannelMeetingIds).
        return await _instance.LoadOpenMeetingMappingsAsync();
    }

    public void RemoveMeetingMapping(long channelId)
    {
        _instance.RemoveMeetingMapping(channelId);
        _resolutions.TryRemove(channelId, out _);
    }

    public Task CloseOrphanedSessionsAsync(int minParticipants) =>
        _instance.CloseOrphanedSessionsAsync(minParticipants);

    // ================= BYO resolution =================

    /// <summary>
    /// Drops every cached resolution for a planet's channels — called when its
    /// voice config changes so live paths stop using stale credentials.
    /// </summary>
    public void InvalidatePlanet(long planetId)
    {
        foreach (var (channelId, resolution) in _resolutions)
        {
            if (resolution.PlanetId == planetId)
                _resolutions.TryRemove(channelId, out _);
        }
    }

    /// <summary>
    /// Resolves a channel to its planet's BYO LiveKit credentials, or null when
    /// the channel runs on the instance backend. Cached (positive and negative)
    /// with a TTL; misses cost one channel + one config lookup.
    /// </summary>
    private async Task<LiveKitCredentials?> ResolveAsync(long channelId, long? knownPlanetId = null)
    {
        if (_resolutions.TryGetValue(channelId, out var cached) && !cached.Expired)
            return cached.Credentials;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();

            var planetId = knownPlanetId;
            if (planetId is null)
            {
                planetId = await db.Channels
                    .AsNoTracking()
                    .Where(x => x.Id == channelId)
                    .Select(x => x.PlanetId)
                    .FirstOrDefaultAsync();
            }

            if (planetId is null or 0)
            {
                _resolutions[channelId] = new ChannelResolution(0, null, DateTime.UtcNow);
                return null;
            }

            var config = await db.PlanetVoiceConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.PlanetId == planetId && x.Enabled);

            var creds = config is null
                ? (LiveKitCredentials?)null
                : PlanetVoiceService.ToCredentials(config, _protector);

            _resolutions[channelId] = new ChannelResolution(planetId.Value, creds, DateTime.UtcNow);
            return creds;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve voice backend for channel {ChannelId}", channelId);
            // Fail toward the instance backend rather than blocking the call.
            return null;
        }
    }
}
