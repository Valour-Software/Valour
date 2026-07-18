namespace Valour.Shared.Models;

/// <summary>
/// Which real-time media backend an instance uses for voice/video channels.
/// The wire value (see <see cref="VoiceProviderExtensions"/>) is carried in the
/// instance manifest and the voice token response so the client knows which
/// interop module to load.
/// </summary>
public enum VoiceProvider
{
    /// <summary>No voice backend configured; voice/video is disabled.</summary>
    None = 0,

    /// <summary>Cloudflare RealtimeKit (managed SaaS). The default.</summary>
    RealtimeKit = 1,

    /// <summary>Self-hostable LiveKit SFU. Media stays on the operator's own infrastructure.</summary>
    LiveKit = 2,
}

public static class VoiceProviderExtensions
{
    public const string RealtimeKitWire = "realtimekit";
    public const string LiveKitWire = "livekit";

    /// <summary>Stable lowercase wire identifier used in JSON and route selection.</summary>
    public static string ToWire(this VoiceProvider provider) => provider switch
    {
        VoiceProvider.RealtimeKit => RealtimeKitWire,
        VoiceProvider.LiveKit => LiveKitWire,
        _ => "none",
    };

    public static VoiceProvider FromWire(string? wire) => wire?.Trim().ToLowerInvariant() switch
    {
        RealtimeKitWire => VoiceProvider.RealtimeKit,
        LiveKitWire => VoiceProvider.LiveKit,
        _ => VoiceProvider.None,
    };
}
