using System.Text.Json.Serialization;

namespace Valour.Client.Components.Calls;

public class RealtimeKitInitOptions
{
    [JsonPropertyName("authToken")]
    public string AuthToken { get; set; } = string.Empty;

    [JsonPropertyName("baseURI")]
    public string? BaseUri { get; set; }

    [JsonPropertyName("defaults")]
    public RealtimeKitMediaDefaults? Defaults { get; set; }

    [JsonPropertyName("modules")]
    public RealtimeKitModules? Modules { get; set; }

    [JsonPropertyName("cachedUserDetails")]
    public RealtimeKitCachedUserDetails? CachedUserDetails { get; set; }
}

public class RealtimeKitMediaDefaults
{
    [JsonPropertyName("audio")]
    public bool? Audio { get; set; }

    [JsonPropertyName("video")]
    public bool? Video { get; set; }
}

public class RealtimeKitModules
{
    [JsonPropertyName("tracing")]
    public bool? Tracing { get; set; }

    [JsonPropertyName("experimentalAudioPlayback")]
    public bool? ExperimentalAudioPlayback { get; set; }
}

public class RealtimeKitCachedUserDetails
{
    [JsonPropertyName("peerId")]
    public string? PeerId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("picture")]
    public string? Picture { get; set; }
}

public class RealtimeKitDeviceSelection
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("groupId")]
    public string? GroupId { get; set; }
}

public class RealtimeKitSelfState
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("picture")]
    public string? Picture { get; set; }

    [JsonPropertyName("audioEnabled")]
    public bool AudioEnabled { get; set; }

    [JsonPropertyName("videoEnabled")]
    public bool VideoEnabled { get; set; }

    [JsonPropertyName("screenShareEnabled")]
    public bool ScreenShareEnabled { get; set; }
}

public class RealtimeKitParticipantsSnapshot
{
    [JsonPropertyName("activeSpeakerPeerId")]
    public string? ActiveSpeakerPeerId { get; set; }

    [JsonPropertyName("participants")]
    public RealtimeKitParticipantState[] Participants { get; set; } = Array.Empty<RealtimeKitParticipantState>();
}

public class RealtimeKitParticipantState
{
    [JsonPropertyName("peerId")]
    public string? PeerId { get; set; }

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("customParticipantId")]
    public string? CustomParticipantId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("picture")]
    public string? Picture { get; set; }

    [JsonPropertyName("audioEnabled")]
    public bool AudioEnabled { get; set; }

    [JsonPropertyName("videoEnabled")]
    public bool VideoEnabled { get; set; }

    [JsonPropertyName("screenShareEnabled")]
    public bool ScreenShareEnabled { get; set; }

    [JsonPropertyName("hasAudioTrack")]
    public bool HasAudioTrack { get; set; }

    [JsonPropertyName("audioTrackId")]
    public string? AudioTrackId { get; set; }

    [JsonPropertyName("isSelf")]
    public bool IsSelf { get; set; }
}
