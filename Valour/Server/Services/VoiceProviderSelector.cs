using Valour.Config.Configs;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Decides which voice backend an instance runs, from config. Pure logic, split
/// out from DI registration so it can be unit-tested: an explicit
/// <c>Voice__Provider</c> always wins; otherwise LiveKit is chosen only when it
/// is configured and RealtimeKit is not, so the managed default is never
/// silently switched.
/// </summary>
public static class VoiceProviderSelector
{
    public static VoiceProvider Resolve(VoiceConfig voice, bool realtimeKitConfigured)
    {
        var explicitProvider = VoiceProviderExtensions.FromWire(voice?.Provider);
        if (explicitProvider == VoiceProvider.LiveKit)
            return VoiceProvider.LiveKit;
        if (explicitProvider == VoiceProvider.RealtimeKit)
            return VoiceProvider.RealtimeKit;

        var liveKitConfigured = voice?.LiveKitConfigured == true;
        return liveKitConfigured && !realtimeKitConfigured
            ? VoiceProvider.LiveKit
            : VoiceProvider.RealtimeKit;
    }
}
