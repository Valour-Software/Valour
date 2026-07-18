namespace Valour.Shared.Models;

/// <summary>
/// Public (secret-free) view of a planet's bring-your-own-voice config.
/// </summary>
public class PlanetVoiceInfo
{
    public long PlanetId { get; set; }

    /// <summary>
    /// Client-facing LiveKit websocket URL members connect to directly.
    /// </summary>
    public string LiveKitUrl { get; set; }

    public string ApiKey { get; set; }

    public bool Enabled { get; set; }

    public DateTime? VerifiedAt { get; set; }
}

/// <summary>
/// Write request for a planet's voice config. The API secret is write-only:
/// leave null/empty on update to keep the stored (encrypted) secret.
/// </summary>
public class PlanetVoiceConfigRequest
{
    public string LiveKitUrl { get; set; }
    public string ApiKey { get; set; }
    public string ApiSecret { get; set; }
    public bool Enabled { get; set; }
}
