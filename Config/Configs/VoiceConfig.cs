namespace Valour.Config.Configs;

/// <summary>
/// Voice/video backend selection. Valour ships two drivers:
/// - RealtimeKit (Cloudflare, managed SaaS) — configured via <see cref="CloudflareConfig"/>.
/// - LiveKit (self-hostable SFU) — configured here.
///
/// The active provider is resolved from <see cref="Provider"/> when set, else
/// auto-selected: LiveKit when it is configured and RealtimeKit is not. This lets
/// a self-hoster get voice working by only setting the Voice__LiveKit* variables,
/// while the official/managed deployment keeps using RealtimeKit unchanged.
/// </summary>
public class VoiceConfig
{
    public static VoiceConfig Current;

    public VoiceConfig()
    {
        Current = this;
    }

    /// <summary>
    /// Explicit backend selection: "realtimekit" or "livekit". Empty = auto-select.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Client-facing LiveKit SFU websocket URL, e.g. "wss://voice.example.com".
    /// Handed to the browser so it can connect directly to the SFU.
    /// </summary>
    public string LiveKitUrl { get; set; } = string.Empty;

    /// <summary>
    /// Server-to-server LiveKit API base for room administration (ListParticipants,
    /// RemoveParticipant, DeleteRoom). Optional — defaults to the http(s) form of
    /// <see cref="LiveKitUrl"/>. Useful when the server reaches LiveKit over an
    /// internal address distinct from the public websocket URL.
    /// </summary>
    public string LiveKitApiUrl { get; set; } = string.Empty;

    /// <summary>LiveKit API key (the token issuer).</summary>
    public string LiveKitApiKey { get; set; } = string.Empty;

    /// <summary>
    /// LiveKit API secret used to sign access tokens (HS256). Must be at least
    /// 32 bytes for the signer; LiveKit-generated secrets already are.
    /// </summary>
    public string LiveKitApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// Dev/LAN mode: allow planet owners to register plain-ws/private-network
    /// LiveKit URLs for bring-your-own-voice, disabling the SSRF protections.
    /// Never enable on public deployments.
    /// </summary>
    public bool AllowInsecurePlanetVoice { get; set; }

    /// <summary>
    /// True when the LiveKit driver has everything it needs to mint tokens.
    /// </summary>
    public bool LiveKitConfigured =>
        !string.IsNullOrWhiteSpace(LiveKitUrl) &&
        !string.IsNullOrWhiteSpace(LiveKitApiKey) &&
        !string.IsNullOrWhiteSpace(LiveKitApiSecret);
}
