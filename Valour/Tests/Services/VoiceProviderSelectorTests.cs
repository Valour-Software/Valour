using Valour.Config.Configs;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

/// <summary>
/// Guards the voice-backend selection rule (explicit wins; auto-select prefers
/// LiveKit only when it's configured and RealtimeKit is not). Pure, no I/O.
///
/// Constructing VoiceConfig writes the global VoiceConfig.Current, so this shares
/// the "VoiceProviderState" collection to stay serialized with the live test.
/// </summary>
[Collection("VoiceProviderState")]
public class VoiceProviderSelectorTests
{
    private static VoiceConfig LiveKitConfigured(string provider = "") => new()
    {
        Provider = provider,
        LiveKitUrl = "wss://voice.example.com",
        LiveKitApiKey = "key",
        LiveKitApiSecret = "01234567890123456789012345678901",
    };

    [Fact]
    public void Explicit_LiveKit_Wins_EvenWhenRealtimeKitConfigured()
    {
        var result = VoiceProviderSelector.Resolve(new VoiceConfig { Provider = "livekit" }, realtimeKitConfigured: true);
        Assert.Equal(VoiceProvider.LiveKit, result);
    }

    [Fact]
    public void Explicit_RealtimeKit_Wins_EvenWhenLiveKitConfigured()
    {
        var result = VoiceProviderSelector.Resolve(LiveKitConfigured("realtimekit"), realtimeKitConfigured: false);
        Assert.Equal(VoiceProvider.RealtimeKit, result);
    }

    [Fact]
    public void Auto_PicksLiveKit_WhenConfigured_AndRealtimeKitIsNot()
    {
        var result = VoiceProviderSelector.Resolve(LiveKitConfigured(), realtimeKitConfigured: false);
        Assert.Equal(VoiceProvider.LiveKit, result);
    }

    [Fact]
    public void Auto_KeepsRealtimeKit_WhenBothConfigured()
    {
        // The managed default is never silently switched.
        var result = VoiceProviderSelector.Resolve(LiveKitConfigured(), realtimeKitConfigured: true);
        Assert.Equal(VoiceProvider.RealtimeKit, result);
    }

    [Fact]
    public void Auto_FallsBackToRealtimeKit_WhenNeitherConfigured()
    {
        var result = VoiceProviderSelector.Resolve(new VoiceConfig(), realtimeKitConfigured: false);
        Assert.Equal(VoiceProvider.RealtimeKit, result);
    }

    [Fact]
    public void Auto_KeepsRealtimeKit_WhenOnlyRealtimeKitConfigured()
    {
        var result = VoiceProviderSelector.Resolve(new VoiceConfig(), realtimeKitConfigured: true);
        Assert.Equal(VoiceProvider.RealtimeKit, result);
    }
}
