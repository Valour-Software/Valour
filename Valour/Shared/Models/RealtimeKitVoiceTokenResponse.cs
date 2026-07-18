namespace Valour.Shared.Models;

/// <summary>
/// Response to a voice join request. Provider-neutral: it carries whatever the
/// selected backend needs to connect. For RealtimeKit that is a meeting id plus
/// an embedded auth token; for LiveKit it is a room name, a signed access token,
/// and the SFU websocket <see cref="Url"/>. The <see cref="Provider"/>
/// discriminator tells the client which interop module to load.
/// </summary>
public class RealtimeKitVoiceTokenResponse
{
    /// <summary>
    /// Wire id of the backend that issued this token ("realtimekit" | "livekit").
    /// See <see cref="VoiceProviderExtensions"/>.
    /// </summary>
    public string Provider { get; set; } = VoiceProviderExtensions.RealtimeKitWire;

    /// <summary>
    /// RealtimeKit: the meeting id. LiveKit: the room name. Empty when waiting for a peer.
    /// </summary>
    public string MeetingId { get; set; } = string.Empty;

    /// <summary>
    /// RealtimeKit: the participant id. LiveKit: the participant identity (userId[:sessionId]).
    /// </summary>
    public string ParticipantId { get; set; } = string.Empty;

    /// <summary>
    /// The token the client presents to the media backend. RealtimeKit auth token
    /// or LiveKit signed access token.
    /// </summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>
    /// Backend connection endpoint. Empty for RealtimeKit (the SDK derives its own
    /// host); the LiveKit SFU websocket URL (wss://...) for LiveKit.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// True when this call runs on the planet's own community-hosted SFU
    /// (bring-your-own-voice) rather than the instance's backend. Drives the
    /// community-voice warning in the call UI.
    /// </summary>
    public bool SelfHosted { get; set; }

    public bool WaitingForPeer { get; set; }

    public int ParticipantCount { get; set; }
}
