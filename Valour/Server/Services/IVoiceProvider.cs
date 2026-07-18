using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// A voice/video backend. Two implementations exist — <see cref="RealtimeKitService"/>
/// (Cloudflare, managed) and <see cref="LiveKitService"/> (self-hostable). The
/// active one is selected in DI from <c>VoiceConfig</c>. The signalling API and the
/// voice-state cleanup worker depend on this interface rather than a concrete driver.
///
/// Presence truth lives in Valour's own Redis heartbeat, not the provider — so the
/// only provider-specific reconciliation primitive needed is "who is actually
/// connected to this channel's meeting right now" (<see cref="GetConnectedUserIdsAsync"/>).
/// </summary>
public interface IVoiceProvider
{
    /// <summary>Which backend this is.</summary>
    VoiceProvider Kind { get; }

    /// <summary>True when the backend has the configuration it needs to issue tokens.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Issues a join token for a user in a channel. Creates/looks up the backing
    /// meeting/room as needed. For a session-aware kick later, <paramref name="sessionId"/>
    /// is folded into the participant identity.
    /// </summary>
    Task<TaskResult<RealtimeKitVoiceTokenResponse>> CreateParticipantTokenAsync(
        Channel channel, long userId, string displayName, string? sessionId);

    /// <summary>Best-effort: eject every live session of a user from a channel's meeting.</summary>
    Task KickUserFromTrackedChannelAsync(long channelId, long userId);

    /// <summary>Best-effort: eject one specific session of a user (falls back to all when no session id).</summary>
    Task KickUserSessionFromTrackedChannelAsync(long channelId, long userId, string? sessionId);

    /// <summary>Best-effort: tear down a channel's meeting so stale tokens cannot rejoin it.</summary>
    Task CloseTrackedMeetingAsync(long channelId, string reason);

    /// <summary>Snapshot of channel → meeting/room id currently tracked in memory.</summary>
    IReadOnlyDictionary<long, string> GetTrackedChannelMeetingIds();

    /// <summary>Loads open channel → meeting/room mappings from durable state (DB or the SFU).</summary>
    Task<Dictionary<long, string>> LoadOpenMeetingMappingsAsync();

    /// <summary>Drops a channel's in-memory meeting mapping.</summary>
    void RemoveMeetingMapping(long channelId);

    /// <summary>Closes a specific meeting/room by id.</summary>
    Task<TaskResult> CloseMeetingAsync(string meetingId, string reason, long? channelId = null);

    /// <summary>
    /// The backend's authoritative view of who is connected to a channel's meeting.
    /// Returns the set of Valour user ids, or null when the backend could not be
    /// queried (in which case reconciliation for that channel is skipped so live
    /// participants are never wrongly removed).
    /// </summary>
    Task<HashSet<long>?> GetConnectedUserIdsAsync(long channelId, string meetingId);

    /// <summary>
    /// Provider-specific sweep for orphaned/underpopulated live sessions the tracked
    /// map doesn't know about. RealtimeKit needs this (server-side meetings linger);
    /// LiveKit rooms auto-expire via empty_timeout, so its implementation is a no-op.
    /// </summary>
    Task CloseOrphanedSessionsAsync(int minParticipants);
}
